// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Recommendations.Embeddings;

namespace App.Recommendations.Radio;

public record QueueRequest(
    int CurrentRow,
    TasteModel Taste,
    IReadOnlySet<Guid> Exclude,
    double ExploreRatio,
    IReadOnlyDictionary<Guid, double> TransitionsFrom,
    int Size,
    DateTimeOffset Now,
    int Seed,
    float[]? Session = null,
    float[]? Mood = null);

public record QueueItem(
    Guid TrackId,
    int Row,
    double Score,
    double CosineTaste,
    bool Explore,
    bool NewBoost);

public static class QueueBuilder
{
    private const double TasteWeight = 0.55;

    private const double CurrentWeight = 0.35;

    private const double TransitionWeight = 0.20;

    // Вектор текущей сессии: дослушанное за последние минуты притягивает, брошенное — отталкивает.
    private const double SessionWeight = 0.30;

    private const double NewBoostBeta = 0.25;

    private const double NewBoostTauDays = 14.0;

    private const double NewTrackDays = 14.0;

    private const int NewBoostSkipGate = 3;

    private const double NewShareCap = 0.3;

    // Радио по настроению берёт только верхние 30% библиотеки по рангу настроения.
    public const float MoodFloor = 0.7f;

    private const double MoodWeight = 0.5;

    public static IReadOnlyList<QueueItem> Build(
        EmbeddingSnapshot snapshot,
        QueueRequest request)
    {
        if (snapshot.IsEmpty || request.Size <= 0)
            return [];

        var random = new Random(request.Seed);
        var size = request.Size;

        var tasteSimilarities = request.Taste.SimilaritiesIn(snapshot);

        var sessionSimilarities = request.Session is { Length: > 0 } session && session.Length == snapshot.Dimension
            ? snapshot.SimilaritiesTo(session)
            : null;

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

        var mood = request.Mood is { } ranks && ranks.Length == snapshot.Count ? ranks : null;
        if (mood is not null)
            allowed = MoodPool(allowed, mood, size);

        if (allowed.Count == 0)
            return [];

        var threshold = VectorMath.Quantile([.. allowed.Select(row => tasteSimilarities[row])], RecommendationTuning.Exploration.FarQuantile);

        var near = new List<Candidate>(allowed.Count);
        var far = new List<Candidate>();

        foreach (var row in allowed)
        {
            var meta = snapshot.MetaAt(row);
            var taste = tasteSimilarities[row];
            var toCurrent = currentSimilarities[row];

            var ageDays = Math.Max(0, (request.Now - meta.CreatedAt).TotalDays);
            var boost = meta.SkippedEarlyCount >= NewBoostSkipGate || meta.CreatedAt == default || ageDays > NewTrackDays
                ? 0
                : NewBoostBeta * Math.Exp(-ageDays / NewBoostTauDays);

            var transition = maxTransition > 0 && request.TransitionsFrom.TryGetValue(meta.TrackId, out var weight) && weight > 0
                ? TransitionWeight * (Math.Log(1 + weight) / Math.Log(1 + maxTransition))
                : 0;

            var score = TasteWeight * taste + CurrentWeight * toCurrent + boost + transition
                        + (sessionSimilarities is null ? 0 : SessionWeight * sessionSimilarities[row])
                        + (mood is null ? 0 : MoodWeight * mood[row]);

            near.Add(new Candidate(row, meta, score, taste, boost, Explore: false));

            if (!(taste <= threshold)) continue;
            var farScore = -taste + random.NextDouble() * RecommendationTuning.Exploration.FarJitter;

            far.Add(new Candidate(row, meta, farScore, taste, boost, Explore: true));
        }

        near.Sort(static (left, right) => right.Score.CompareTo(left.Score));
        far.Sort(static (left, right) => right.Score.CompareTo(left.Score));

        var farWanted = (int)Math.Round(size * request.ExploreRatio, MidpointRounding.AwayFromZero);

        if (size >= 3 && request.ExploreRatio > 0 && farWanted < 1)
            farWanted = 1;

        farWanted = Math.Min(farWanted, size * 2 / 3);

        // Отбор: без повторов, не больше MaxPerArtist на артиста, без дублей по хэшу и «той же песне»,
        // новинок не больше доли NewShareCap.
        var newCap = (int)Math.Ceiling(size * NewShareCap);
        var used = new HashSet<int>();
        var artists = new Dictionary<Guid, int>();
        var hashes = string.IsNullOrEmpty(current.ContentHash) ? new HashSet<string>() : [current.ContentHash];
        var songs = string.IsNullOrEmpty(current.SongKey) ? new HashSet<string>() : [current.SongKey];
        var newTaken = 0;

        var nearPicked = Take(near, size - farWanted, explore: false);
        var farPicked = Take(far, farWanted, explore: true);

        // Если строгие лимиты не дали набрать очередь, добираем из ближних без лимита на артиста и новинки.
        var missing = size - nearPicked.Count - farPicked.Count;
        foreach (var candidate in near.TakeWhile(_ => missing > 0).Where(candidate => !used.Contains(candidate.Row) && !IsDuplicate(candidate)))
        {
            Accept(candidate);
            nearPicked.Add(candidate with { Boost = 0 });
            missing--;
        }

        // Разведка вкрапляется в ближние через равные промежутки.
        if (farPicked.Count == 0)
            return [.. nearPicked.Select(item => item.ToItem())];

        if (nearPicked.Count == 0)
            return [.. farPicked.Select(item => item.ToItem())];

        var gap = Math.Max(2, nearPicked.Count / farPicked.Count);
        var result = new List<QueueItem>(nearPicked.Count + farPicked.Count);
        var nextFar = 0;
        var sinceFar = 0;

        foreach (var candidate in nearPicked)
        {
            result.Add(candidate.ToItem());
            sinceFar++;

            if (nextFar >= farPicked.Count || sinceFar < gap) continue;
            result.Add(farPicked[nextFar++].ToItem());
            sinceFar = 0;
        }

        while (nextFar < farPicked.Count)
            result.Add(farPicked[nextFar++].ToItem());

        return result;

        List<Candidate> Take(List<Candidate> source, int wanted, bool explore)
        {
            var taken = new List<Candidate>(Math.Max(0, wanted));

            foreach (var candidate in source)
            {
                if (taken.Count >= wanted)
                    break;

                if (used.Contains(candidate.Row)
                    || artists.GetValueOrDefault(candidate.Meta.ArtistId) >= RecommendationTuning.Diversity.MaxPerArtist
                    || IsDuplicate(candidate)
                    || (candidate.IsNew && newTaken >= newCap))
                    continue;

                Accept(candidate);
                taken.Add(candidate with { Explore = explore });
            }

            return taken;
        }

        bool IsDuplicate(Candidate candidate) =>
            (!string.IsNullOrEmpty(candidate.Meta.ContentHash) && hashes.Contains(candidate.Meta.ContentHash))
            || (!string.IsNullOrEmpty(candidate.Meta.SongKey) && songs.Contains(candidate.Meta.SongKey));

        void Accept(Candidate candidate)
        {
            used.Add(candidate.Row);
            artists[candidate.Meta.ArtistId] = artists.GetValueOrDefault(candidate.Meta.ArtistId) + 1;

            if (!string.IsNullOrEmpty(candidate.Meta.ContentHash))
                hashes.Add(candidate.Meta.ContentHash);

            if (!string.IsNullOrEmpty(candidate.Meta.SongKey))
                songs.Add(candidate.Meta.SongKey);

            if (candidate.IsNew)
                newTaken++;
        }
    }

    // Не меньше двух очередей: в маленькой библиотеке строгий порог оставил бы радио без треков.
    private static List<int> MoodPool(List<int> allowed, float[] mood, int size)
    {
        var pool = allowed.FindAll(row => mood[row] >= MoodFloor);
        var minimum = Math.Min(allowed.Count, size * 2);

        return pool.Count >= minimum ? pool : [.. allowed.OrderByDescending(row => mood[row]).Take(minimum)];
    }

    private readonly record struct Candidate(
        int Row,
        TrackVectorMeta Meta,
        double Score,
        double Taste,
        double Boost,
        bool Explore)
    {
        public bool IsNew => Boost > 0.01;

        public QueueItem ToItem() => new(Meta.TrackId, Row, Score, Taste, Explore, IsNew);
    }
}
