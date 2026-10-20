// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Numerics.Tensors;
using Infrastructure.Persistence;
using App.Abstractions;
using App.Common;
using App.Dtos;
using Microsoft.EntityFrameworkCore;
using Domain.Entities.Recommendations;
using App.Recommendations.Embeddings;
using App.Recommendations.Home;
using App.Recommendations.Moods;

namespace App.Recommendations.Radio;

public class RadioService(
    ApplicationDbContext db,
    ICurrentUser currentUser,
    EmbeddingIndex index,
    MoodCatalog moods,
    SessionTaste sessionTaste,
    DaypartMoods daypartMoods,
    TimeProvider clock,
    ILogger<RadioService> logger)
{
    public const int MaxBatchSize = 20;

    private static readonly TimeSpan RecentWindow = TimeSpan.FromHours(48);

    private const int RecentLimit = 120;

    private const int AnchorCandidates = 20;

    private const double AnchorTemperature = 0.08;

    private const double AnchorRecencyPenalty = 0.35;

    private const double AnchorRandomChance = 0.12;

    // Сколько последних треков очереди клиента учитывать в лимите на артиста между порциями радио.
    private const int RecentArtistWindow = 10;

    // Продолжение «застряло», если артист затравки звучал среди StuckWindow последних треков или все они —
    // из ближайших StuckSoundShare библиотеки к затравке. Тогда якорь уходит к другому центру вкуса.
    private const int StuckWindow = 5;

    private const double StuckSoundShare = 0.05;

    public async Task<RadioBatchDto> NextAsync(RadioRequest request, CancellationToken ct = default)
    {
        if (request.Limit is < 1 or > MaxBatchSize)
            throw new ValidationException("Radio limit must be between 1 and {0}.", MaxBatchSize);

        Mood? mood = null;
        if (request.Mood is { } moodKey)
            mood = moods.Find(moodKey) ?? throw new ValidationException("Unknown mood '{0}'.", moodKey);

        var userId = currentUser.Id;
        var now = clock.GetUtcNow();

        var snapshot = index.Snapshot();
        if (snapshot.IsEmpty)
        {
            logger.LogDebug("Radio found nothing to continue track {SeedTrackId} with", request.SeedTrackId);
            return new RadioBatchDto([], null);
        }

        var profile = await UserRecommendationContext.LoadAsync(db, snapshot, userId, now, ct);
        var taste = profile.Taste;

        // Недавно слушанное и то, что уже стоит в очереди клиента, — вместе с копиями того же трека.
        var since = now - RecentWindow;
        var recent = await db.PlaybackEvents.AsNoTracking()
            .Where(item => item.UserId == userId && item.TrackId != null && item.OccurredAt >= since)
            .GroupBy(item => item.TrackId!.Value)
            .OrderByDescending(group => group.Max(item => item.OccurredAt))
            .Take(RecentLimit)
            .Select(group => group.Key)
            .ToListAsync(ct);

        // Отвергнутое («Не интересно», дважды брошенное в начале) радио не предлагает вовсе.
        var rejected = profile.Ranking.History
            .Where(pair => pair.Value.Score < RecommendationTuning.Penalties.RejectedTrackScore
                           || pair.Value is { SkipCount: >= 2, AverageCompletion: < 0.2 })
            .Select(pair => pair.Key);

        var clientExclude = request.Exclude ?? [];
        var exclude = new HashSet<Guid>(clientExclude);
        foreach (var trackId in recent.Concat(clientExclude).Concat(rejected))
            exclude.UnionWith(snapshot.CloneIds(trackId));

        var session = (await sessionTaste.LoadAsync(userId, snapshot, now, ct)).Vector;
        var moodScores = mood is null ? null : moods.ScoresIn(snapshot, mood);

        // Настроения, которого в библиотеке нет, не играем: лучше пустое радио, чем «что-то похожее».
        if (moodScores is { Playable: false })
        {
            logger.LogDebug("Mood {Mood} has too few fitting tracks for a radio", mood!.Key);
            return new RadioBatchDto([], null);
        }

        // У радио по настроению настроение уже выбрано; время суток подсказывает только обычному радио.
        var context = mood is null ? await daypartMoods.ContextAsync(userId, snapshot, ct) : null;

        var random = new Random(Explorer.SeedFor(userId, "radio", now) ^ (int)(now.Ticks & 0xFFFF));

        // Клиент присылает очередь по порядку, так что её хвост — то, что звучало прямо перед этой порцией.
        var tail = clientExclude
            .Where(trackId => trackId != request.SeedTrackId)
            .TakeLast(RecentArtistWindow)
            .Select(snapshot.RowOf)
            .Where(row => row >= 0)
            .ToList();

        // Якорь: заданный трек, иначе изредка случайный, иначе сэмпл (softmax) из ближайших к одному
        // из центров вкуса (центр выбирается пропорционально его доле), где недавно слушанные приглушены.
        var anchorRow = -1;
        var moved = false;

        // Радио по настроению якорится на настроение каждую порцию, а не на последний трек: иначе очередь
        // цепочкой «похоже на прошлый» уползает к краю настроения.
        if (moodScores is not null)
        {
            anchorRow = MoodAnchor(snapshot, moodScores, taste, exclude, random);
        }
        else if (request.SeedTrackId is { } seed && snapshot.RowOf(seed) is var seeded and >= 0)
        {
            anchorRow = seeded;

            // Цепочка «похоже на прошлый» застряла на одном артисте или звучании: порция начинается
            // с трека у другого центра вкуса — не этих артистов и не из ближайших к затравке.
            if (!taste.IsEmpty && Stuck(snapshot, seeded, tail) is { } stuck)
            {
                var mode = OtherMode(taste, snapshot.Vector(seeded), random);
                var fresh = TasteAnchor(snapshot, mode.Centre, exclude, random,
                    row => exclude.Contains(snapshot.MetaAt(row).TrackId) || stuck(row));

                if (fresh >= 0)
                {
                    anchorRow = fresh;
                    moved = true;
                }
            }
        }
        else if (!taste.IsEmpty && random.NextDouble() < AnchorRandomChance && snapshot.Count > 8)
        {
            anchorRow = random.Next(snapshot.Count);
        }
        else if (!taste.IsEmpty)
        {
            anchorRow = TasteAnchor(snapshot, PickMode(taste, random).Centre, exclude, random);
        }

        var anchorId = anchorRow >= 0 ? snapshot.MetaAt(anchorRow).TrackId : (Guid?)null;
        IReadOnlyDictionary<Guid, double> transitions = new Dictionary<Guid, double>();

        if (anchorId is { } from)
        {
            exclude.UnionWith(snapshot.CloneIds(from));

            // Что пользователь сам включал следом за якорем: соседние старты в одной сессии с разрывом
            // не больше получаса, каждый переход затухает с полураспадом TransitionHalfLifeDays.
            // Порядок — по времени клиента: sequence внутри одного батча вставки не отражает порядок событий.
            var halfLifeSeconds = RecommendationTuning.Decay.TransitionHalfLifeDays * 86400;
            var started = (int)PlaybackEventType.TrackStarted;
            var maxGap = TimeSpan.FromMinutes(30);

            transitions = (await db.Database.SqlQuery<TransitionRow>(
                    $"""
                    SELECT next_track_id AS to_track_id,
                           SUM(power(0.5, EXTRACT(EPOCH FROM ({now} - occurred_at)) / {halfLifeSeconds})) AS weight
                    FROM (
                        SELECT track_id, occurred_at,
                               LEAD(track_id) OVER session AS next_track_id,
                               LEAD(occurred_at) OVER session AS next_occurred_at
                        FROM playback_events
                        WHERE user_id = {userId} AND type = {started} AND track_id IS NOT NULL
                          AND session_id IN (
                              SELECT session_id FROM playback_events
                              WHERE user_id = {userId} AND type = {started} AND track_id = {from})
                        WINDOW session AS (PARTITION BY session_id ORDER BY occurred_at, sequence)
                    ) pairs
                    WHERE track_id = {from}
                      AND next_track_id <> track_id
                      AND next_occurred_at - occurred_at <= {maxGap}
                    GROUP BY next_track_id
                    HAVING SUM(power(0.5, EXTRACT(EPOCH FROM ({now} - occurred_at)) / {halfLifeSeconds})) >= 1
                    ORDER BY weight DESC
                    LIMIT 200
                    """)
                .ToListAsync(ct))
                .ToDictionary(row => row.ToTrackId, row => row.Weight);
        }

        // Затравка, от которой ушёл якорь, уже звучит — она тоже в счёте артистов.
        var recentArtists = tail
            .Concat(moved ? [snapshot.RowOf(request.SeedTrackId!.Value)] : [])
            .SelectMany(row => snapshot.MetaAt(row).Artists)
            .ToList();

        var size = request.Limit ?? RecommendationTuning.Exploration.QueueSize;

        var queue = QueueBuilder.Build(snapshot, new QueueRequest(
            CurrentRow: anchorRow,
            Taste: taste,
            Exclude: exclude,
            // Пока вкус не сложился, радио больше разведывает.
            ExploreRatio: profile.Maturity switch
            {
                ProfileMaturity.Cold => Math.Max(
                    RecommendationTuning.Exploration.QueueDiscoverRatio, RecommendationTuning.Exploration.QueueRatio),
                ProfileMaturity.Warm => (RecommendationTuning.Exploration.QueueRatio + RecommendationTuning.Exploration.QueueDiscoverRatio) / 2,
                _ => RecommendationTuning.Exploration.QueueRatio,
            },
            TransitionsFrom: transitions,
            // Новый якорь сам открывает порцию.
            Size: moved ? size - 1 : size,
            Now: now,
            Seed: random.Next(),
            Session: session,
            Mood: moodScores,
            Context: context,
            RecentArtists: recentArtists));

        if (queue.Count == 0 && !moved)
        {
            logger.LogDebug("Radio found nothing to continue track {SeedTrackId} with", request.SeedTrackId);
            return new RadioBatchDto([], anchorId);
        }

        var tracks = await db.TracksByIdAsync(
            userId, anchorId is { } anchor ? queue.Select(item => item.TrackId).Append(anchor) : queue.Select(item => item.TrackId), ct);

        var anchorTitle = anchorId is { } anchorTrack && tracks.TryGetValue(anchorTrack, out var anchorDto)
            ? anchorDto.Title
            : null;

        var opening = moved && tracks.TryGetValue(anchorId!.Value, out var movedTo)
            ? new RecommendedTrackDto(
                movedTo,
                new RecommendationReasonDto(ReasonKinds.MatchesYourTaste, null, null),
                null,
                new QueueSignalsDto(false))
            : null;

        var result = queue
            .Where(item => tracks.ContainsKey(item.TrackId))
            .Select(item => new RecommendedTrackDto(
                tracks[item.TrackId],
                new RecommendationReasonDto(
                    item.Explore ? ReasonKinds.Discovery
                    : anchorTitle is null ? ReasonKinds.MatchesYourTaste
                    : ReasonKinds.SoundsLike,
                    item.Explore ? null : anchorTitle,
                    anchorId),
                null,
                new QueueSignalsDto(item.Explore)))
            .Prepend(opening)
            .OfType<RecommendedTrackDto>()
            .ToList();

        return new RadioBatchDto(result, anchorId);
    }

    // Первый трек радио по настроению: из самых подходящих по настроению — ближе всего ко вкусу
    // (без вкуса — просто по настроению), со случайностью, чтобы каждый запуск начинался по-разному.
    private static int MoodAnchor(
        EmbeddingSnapshot snapshot, MoodScores mood, TasteModel taste, IReadOnlySet<Guid> exclude, Random random)
    {
        var tasteSimilarities = taste.IsEmpty ? null : taste.SimilaritiesIn(snapshot);

        var candidates = Enumerable.Range(0, snapshot.Count)
            .Where(row => mood.Members[row] && !exclude.Contains(snapshot.MetaAt(row).TrackId))
            .Select(row => (Row: row, Score: mood.Ranks[row] + (tasteSimilarities?[row] ?? 0)))
            .OrderByDescending(item => item.Score)
            .Take(AnchorCandidates)
            .ToList();

        return candidates.Count == 0 ? -1 : candidates[random.Next(candidates.Count)].Row;
    }

    // Сэмпл (softmax) из ближайших к центру вкуса, где недавно слушанные приглушены; skip отсекает строки совсем.
    private static int TasteAnchor(
        EmbeddingSnapshot snapshot, float[] centre, IReadOnlySet<Guid> exclude, Random random, Func<int, bool>? skip = null)
    {
        IReadOnlyList<ScoredRow> nearest;
        if (skip is null)
        {
            nearest = snapshot.TopK(centre, AnchorCandidates);
        }
        else
        {
            var similarities = snapshot.SimilaritiesTo(centre);

            nearest = Enumerable.Range(0, snapshot.Count)
                .Where(row => !skip(row))
                .OrderByDescending(row => similarities[row])
                .Take(AnchorCandidates)
                .Select(row => new ScoredRow(row, snapshot.MetaAt(row).TrackId, similarities[row]))
                .ToList();
        }

        if (nearest.Count == 0)
            return -1;

        var scores = nearest.All(hit => exclude.Contains(hit.TrackId))
            ? nearest.Select(hit => (double)hit.Score).ToArray()
            : nearest.Select(hit => hit.Score - (exclude.Contains(hit.TrackId) ? AnchorRecencyPenalty : 0)).ToArray();

        var best = scores.Max();
        var weights = scores.Select(score => Math.Exp((score - best) / AnchorTemperature)).ToArray();
        var threshold = random.NextDouble() * weights.Sum();

        for (var i = 0; i < weights.Length; i++)
        {
            threshold -= weights[i];
            if (threshold <= 0)
                return nearest[i].Row;
        }

        return nearest[0].Row;
    }

    // null — продолжение не застряло; иначе — какие строки не годятся в новый якорь: треки артистов затравки
    // и ближайшие к затравке по звучанию.
    private static Func<int, bool>? Stuck(EmbeddingSnapshot snapshot, int seedRow, IReadOnlyList<int> tail)
    {
        var recent = tail.TakeLast(StuckWindow).ToList();
        if (recent.Count == 0)
            return null;

        var seedArtists = snapshot.MetaAt(seedRow).Artists;
        var sameArtist = recent.Any(row => snapshot.MetaAt(row).Artists.Any(seedArtists.Contains));

        var similarities = snapshot.SimilaritiesTo(snapshot.Vector(seedRow));
        var close = VectorMath.Quantile(similarities, 1 - StuckSoundShare);
        var sameSound = recent.Count == StuckWindow && recent.All(row => similarities[row] >= close);

        if (!sameArtist && !sameSound)
            return null;

        return row => similarities[row] >= close || snapshot.MetaAt(row).Artists.Any(seedArtists.Contains);
    }

    // Центр вкуса, далёкий от затравки: среди остальных — пропорционально доле; если центр один — он.
    private static TasteMode OtherMode(TasteModel taste, ReadOnlySpan<float> seed, Random random)
    {
        var nearest = taste.NearestMode(seed);
        var others = taste.Modes.Where((_, index) => index != nearest).ToList();
        if (others.Count == 0)
            return taste.Modes[0];

        var roll = random.NextDouble() * others.Sum(mode => mode.Share);
        foreach (var mode in others)
        {
            roll -= mode.Share;
            if (roll < 0)
                return mode;
        }

        return others[^1];
    }

    private static TasteMode PickMode(TasteModel taste, Random random)
    {
        var roll = random.NextDouble();

        foreach (var mode in taste.Modes)
        {
            roll -= mode.Share;
            if (roll < 0)
                return mode;
        }

        return taste.Modes[0];
    }

    private sealed record TransitionRow(Guid ToTrackId, double Weight);
}
