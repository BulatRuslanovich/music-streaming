// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations.Embeddings;

namespace MusicStreaming.Application.Recommendations.Queue;

/// <param name="CurrentRow">Строка играющего трека или -1, когда очередь начинается с нуля.</param>
/// <param name="Taste">Вектор запроса «вкус сейчас»; пустой — вкуса ещё нет.</param>
/// <param name="Exclude">Треки, которые нельзя предлагать: уже слышал, недавно играли, их клоны.</param>
/// <param name="Discover">Режим знакомства: вкусу доверять почти нечему, ведём почти наугад.</param>
/// <param name="TransitionsFrom">Веса рёбер из текущего трека.</param>
public record QueueRequest(
    int CurrentRow,
    float[] Taste,
    IReadOnlySet<Guid> Exclude,
    double ExploreRatio,
    bool Discover,
    IReadOnlyDictionary<Guid, double> TransitionsFrom,
    int Size,
    DateTimeOffset Now,
    int Seed);

/// <param name="Explore">Трек взят из далёкой корзины, а не из близкой.</param>
/// <param name="NewBoost">Сработала надбавка за новизну в библиотеке.</param>
/// <param name="Score">
/// Итоговая оценка. Интерфейсу не нужна — по ней тесты проверяют, что каждый терм вносит ровно
/// столько, сколько обещает.
/// </param>
public record QueueItem(
    Guid TrackId,
    int Row,
    double Score,
    double CosineTaste,
    double CosineCurrent,
    bool Explore,
    bool NewBoost,
    int ClusterId);

/// <summary>
/// Собирает очередь радио: аддитивная оценка, ближняя и дальняя корзины, жёсткие ограничения
/// на однообразие и чередование.
/// <para>
/// Ни базы, ни времени, ни случайности извне — всё приходит в <see cref="QueueRequest"/>.
/// Отсюда и тесты: выбор следующего трека проверяется без поднятия половины сервиса.
/// </para>
/// </summary>
public static class QueueBuilder
{
    /// <summary>Вес близости к вкусу слушателя.</summary>
    private const double TasteWeight = 0.55;

    /// <summary>Вес близости к играющему треку: он и делает очередь потоком, а не списком.</summary>
    private const double CurrentWeight = 0.35;

    /// <summary>В режиме знакомства оба веса падают, а решает шум.</summary>
    private const double DiscoverTasteWeight = 0.15;
    private const double DiscoverCurrentWeight = 0.15;
    private const double DiscoverNoise = 0.7;

    /// <summary>Вес нормированного веса перехода.</summary>
    private const double TransitionWeight = 0.20;

    /// <summary>Надбавка за то, что трек из того же кластера, что и играющий.</summary>
    private const double SameClusterBonus = 0.03;

    /// <summary>Максимум надбавки за новизну в библиотеке.</summary>
    private const double NewBoostBeta = 0.25;

    /// <summary>За сколько дней надбавка за новизну затухает вдвое с небольшим.</summary>
    private const double NewBoostTauDays = 14.0;

    /// <summary>За сколько показов затухает надбавка за новизну.</summary>
    private const double NewBoostGamma = 5.0;

    /// <summary>Старше этого трек новым уже не считается.</summary>
    private const double NewTrackDays = 14.0;

    /// <summary>Столько ранних пропусков — и надбавка снимается совсем.</summary>
    private const int NewBoostSkipGate = 3;

    /// <summary>Какую долю очереди могут занять треки с надбавкой за новизну.</summary>
    private const double NewShareCap = 0.3;
    private const double DiscoverNewShareCap = 0.5;

    /// <summary>
    /// Ширина far-корзины, её разброс и потолок на артиста берутся из настроек, а не из констант
    /// рядом: те же три числа читают полки, и расходиться им незачем.
    /// </summary>
    public static IReadOnlyList<QueueItem> Build(
        EmbeddingSnapshot snapshot, QueueRequest request, RecommendationOptions options)
    {
        if (snapshot.IsEmpty || request.Size <= 0)
            return [];

        var random = new Random(request.Seed);
        var size = request.Size;

        var tasteSimilarities = request.Taste.Length == snapshot.Dimension
            ? snapshot.SimilaritiesTo(request.Taste)
            : new float[snapshot.Count];

        // Без играющего трека «близость к текущему» подменяется близостью к вкусу — иначе терм
        // просто исчез бы, и первая выдача считалась бы по другой формуле, чем все следующие.
        var currentSimilarities = request.CurrentRow >= 0
            ? snapshot.SimilaritiesTo(snapshot.Vector(request.CurrentRow))
            : tasteSimilarities;

        var current = request.CurrentRow >= 0 ? snapshot.MetaAt(request.CurrentRow) : default;
        var maxTransition = request.TransitionsFrom.Count == 0 ? 0 : request.TransitionsFrom.Values.Max();

        var allowed = new List<int>(snapshot.Count);
        for (var row = 0; row < snapshot.Count; row++)
        {
            if (row == request.CurrentRow)
                continue;

            var meta = snapshot.MetaAt(row);
            if (request.Exclude.Contains(meta.TrackId))
                continue;

            if (!string.IsNullOrEmpty(current.ContentHash) && meta.ContentHash == current.ContentHash)
                continue;

            allowed.Add(row);
        }

        if (allowed.Count == 0)
            return [];

        var threshold = Threshold(tasteSimilarities, allowed, options.FarQuantile);

        var near = new List<Candidate>(allowed.Count);
        var far = new List<Candidate>();

        foreach (var row in allowed)
        {
            var meta = snapshot.MetaAt(row);
            var taste = tasteSimilarities[row];
            var toCurrent = currentSimilarities[row];
            var boost = NewBoost(meta, request.Now);
            var transition = TransitionTerm(request.TransitionsFrom, meta.TrackId, maxTransition);

            double score;
            if (request.Discover)
            {
                score = DiscoverTasteWeight * taste
                        + DiscoverCurrentWeight * toCurrent
                        + boost
                        + transition
                        + random.NextDouble() * DiscoverNoise;
            }
            else
            {
                score = TasteWeight * taste + CurrentWeight * toCurrent + boost + transition;

                if (current.ClusterId >= 0 && meta.ClusterId == current.ClusterId)
                    score += SameClusterBonus;
            }

            near.Add(new Candidate(row, meta, score, taste, toCurrent, boost, Explore: false));

            if (taste <= threshold || request.Discover)
            {
                // Далёкая корзина ранжируется «от самого непохожего»: это exploration по
                // звучанию, а не по тому, чего слушатель просто не встречал.
                var farScore = request.Discover
                    ? random.NextDouble()
                    : -taste + random.NextDouble() * options.FarJitter;

                far.Add(new Candidate(row, meta, farScore, taste, toCurrent, boost, Explore: true));
            }
        }

        near.Sort(static (left, right) => right.Score.CompareTo(left.Score));
        far.Sort(static (left, right) => right.Score.CompareTo(left.Score));

        var (nearWanted, farWanted, newCap) = Split(size, request.ExploreRatio, request.Discover);

        var state = new Selection(newCap, options.MaxPerArtist);
        state.Seed(current);

        var nearPicked = state.Take(near, nearWanted, explore: false);
        var farPicked = state.Take(far, farWanted, explore: true);

        // Две послабленные добивки: сначала снимаем квоту на новизну, затем и ограничения
        // на однообразие. Короткая библиотека не должна оставлять очередь пустой.
        if (nearPicked.Count + farPicked.Count < size)
            nearPicked.AddRange(state.TakeRelaxed(near, size - nearPicked.Count - farPicked.Count));

        return Interleave(nearPicked, farPicked);
    }

    private static float Threshold(float[] similarities, List<int> allowed, double quantile)
    {
        var sample = new float[allowed.Count];
        for (var i = 0; i < allowed.Count; i++)
            sample[i] = similarities[allowed[i]];

        return VectorMath.Quantile(sample, quantile);
    }

    /// <summary>
    /// Надбавка за новизну: свежий трек всплывает сам, но гаснет и со временем, и с числом
    /// показов, а трижды бросённый в начале не всплывает вовсе.
    /// </summary>
    private static double NewBoost(TrackVectorMeta meta, DateTimeOffset now)
    {
        if (meta.SkippedEarlyCount >= NewBoostSkipGate || meta.CreatedAt == default)
            return 0;

        var ageDays = Math.Max(0, (now - meta.CreatedAt).TotalDays);
        if (ageDays > NewTrackDays)
            return 0;

        return NewBoostBeta
               * Math.Exp(-ageDays / NewBoostTauDays)
               * Math.Exp(-meta.ShownCount / NewBoostGamma);
    }

    /// <summary>
    /// Вес перехода, сжатый логарифмом и приведённый к максимуму в этой же сборке. Без сжатия
    /// один заезженный стык перебивал бы всё остальное.
    /// </summary>
    private static double TransitionTerm(
        IReadOnlyDictionary<Guid, double> transitions, Guid trackId, double maximum)
    {
        if (maximum <= 0 || !transitions.TryGetValue(trackId, out var weight) || weight <= 0)
            return 0;

        return TransitionWeight * (Math.Log(1 + weight) / Math.Log(1 + maximum));
    }

    private static (int Near, int Far, int NewCap) Split(int size, double exploreRatio, bool discover)
    {
        var far = (int)Math.Round(size * exploreRatio, MidpointRounding.AwayFromZero);

        // Хотя бы один незнакомый трек, если очередь вообще длиннее пары штук.
        if (size >= 3 && exploreRatio > 0 && far < 1)
            far = 1;

        if (discover)
            far = Math.Max(far, size / 2);

        far = Math.Min(far, size * 2 / 3);

        var newCap = (int)Math.Ceiling(size * (discover ? DiscoverNewShareCap : NewShareCap));

        return (size - far, far, newCap);
    }

    /// <summary>
    /// Чередование: далёкие треки расставляются через равные промежутки и никогда не идут
    /// первыми — по первому треку слушатель судит обо всей очереди.
    /// </summary>
    private static List<QueueItem> Interleave(List<Candidate> near, List<Candidate> far)
    {
        if (far.Count == 0)
            return [.. near.Select(item => item.ToItem())];

        if (near.Count == 0)
            return [.. far.Select(item => item.ToItem())];

        var gap = Math.Max(2, near.Count / far.Count);
        var result = new List<QueueItem>(near.Count + far.Count);
        var nextFar = 0;
        var sinceFar = 0;

        foreach (var candidate in near)
        {
            result.Add(candidate.ToItem());
            sinceFar++;

            if (nextFar < far.Count && sinceFar >= gap)
            {
                result.Add(far[nextFar++].ToItem());
                sinceFar = 0;
            }
        }

        while (nextFar < far.Count)
            result.Add(far[nextFar++].ToItem());

        return result;
    }

    private readonly record struct Candidate(
        int Row,
        TrackVectorMeta Meta,
        double Score,
        double Taste,
        double Current,
        double Boost,
        bool Explore)
    {
        public bool IsNew => Boost > 0.01;

        public QueueItem ToItem() => new(
            Meta.TrackId, Row, Score, Taste, Current, Explore, IsNew, Meta.ClusterId);
    }

    /// <summary>
    /// Жадный отбор с жёсткими ограничениями: потолок на артиста, без байт-идентичных копий
    /// и без той же песни под другим файлом.
    /// </summary>
    private sealed class Selection(int newCap, int maxPerArtist)
    {
        private readonly HashSet<int> _used = [];
        private readonly Dictionary<Guid, int> _artists = [];
        private readonly HashSet<string> _hashes = [];
        private readonly HashSet<string> _songs = [];
        private int _new;

        public void Seed(TrackVectorMeta current)
        {
            // Без играющего трека поля структуры пусты — это нормальный старт очереди.
            if (!string.IsNullOrEmpty(current.ContentHash))
                _hashes.Add(current.ContentHash);

            if (!string.IsNullOrEmpty(current.SongKey))
                _songs.Add(current.SongKey);
        }

        public List<Candidate> Take(List<Candidate> source, int wanted, bool explore)
        {
            var taken = new List<Candidate>(Math.Max(0, wanted));

            foreach (var candidate in source)
            {
                if (taken.Count >= wanted)
                    break;

                if (!Allows(candidate) || (candidate.IsNew && _new >= newCap))
                    continue;

                Accept(candidate);
                taken.Add(candidate with { Explore = explore });
            }

            return taken;
        }

        /// <summary>
        /// Добивка без квоты на новизну и без ограничения на артиста: лучше однообразная
        /// очередь, чем короткая.
        /// <para>
        /// Дедупликацию по содержимому и по песне она при этом <b>не</b> снимает: снятая, она
        /// на маленькой библиотеке вернула бы один и тот же трек под разными файлами. Это не
        /// разнообразие, а тождество — повтор здесь хуже, чем недобор.
        /// </para>
        /// </summary>
        public List<Candidate> TakeRelaxed(List<Candidate> source, int wanted)
        {
            var taken = new List<Candidate>(Math.Max(0, wanted));

            foreach (var candidate in source)
            {
                if (taken.Count >= wanted)
                    break;

                if (_used.Contains(candidate.Row) || IsDuplicate(candidate))
                    continue;

                Accept(candidate);

                // Надбавка обнуляется: трек попал сюда не за новизну, и помечать его так
                // значило бы врать интерфейсу.
                taken.Add(candidate with { Boost = 0 });
            }

            return taken;
        }

        private bool Allows(Candidate candidate) =>
            !_used.Contains(candidate.Row)
            && _artists.GetValueOrDefault(candidate.Meta.ArtistId) < maxPerArtist
            && !IsDuplicate(candidate);

        /// <summary>Тот же файл или та же песня под другим файлом.</summary>
        private bool IsDuplicate(Candidate candidate)
        {
            if (!string.IsNullOrEmpty(candidate.Meta.ContentHash) && _hashes.Contains(candidate.Meta.ContentHash))
                return true;

            return !string.IsNullOrEmpty(candidate.Meta.SongKey) && _songs.Contains(candidate.Meta.SongKey);
        }

        private void Accept(Candidate candidate)
        {
            _used.Add(candidate.Row);
            _artists[candidate.Meta.ArtistId] = _artists.GetValueOrDefault(candidate.Meta.ArtistId) + 1;

            if (!string.IsNullOrEmpty(candidate.Meta.ContentHash))
                _hashes.Add(candidate.Meta.ContentHash);

            if (!string.IsNullOrEmpty(candidate.Meta.SongKey))
                _songs.Add(candidate.Meta.SongKey);

            if (candidate.IsNew)
                _new++;
        }
    }
}
