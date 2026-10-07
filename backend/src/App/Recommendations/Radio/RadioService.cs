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

    private static readonly TimeSpan SessionWindow = TimeSpan.FromMinutes(45);

    private const double SessionHalfLifeMinutes = 15;

    public async Task<RadioBatchDto> NextAsync(RadioRequest request, CancellationToken ct = default)
    {
        if (request.Limit is < 1 or > MaxBatchSize)
            throw new ValidationException($"Radio limit must be between 1 and {MaxBatchSize}.");

        Mood? mood = null;
        if (request.Mood is { } moodKey)
            mood = moods.Find(moodKey) ?? throw new ValidationException($"Unknown mood '{moodKey}'.");

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

        var session = await SessionVectorAsync(userId, snapshot, now, ct);
        var moodRanks = mood is null ? null : moods.RanksIn(snapshot, mood);

        var random = new Random(Explorer.SeedFor(userId, "radio", now) ^ (int)(now.Ticks & 0xFFFF));

        // Якорь: заданный трек, иначе изредка случайный, иначе сэмпл (softmax) из ближайших к одному
        // из центров вкуса (центр выбирается пропорционально его доле), где недавно слушанные приглушены.
        var anchorRow = -1;

        if (request.SeedTrackId is { } seed && snapshot.RowOf(seed) is var seeded and >= 0)
        {
            anchorRow = seeded;
        }
        else if (moodRanks is not null)
        {
            anchorRow = MoodAnchor(snapshot, moodRanks, taste, exclude, random);
        }
        else if (!taste.IsEmpty && random.NextDouble() < AnchorRandomChance && snapshot.Count > 8)
        {
            anchorRow = random.Next(snapshot.Count);
        }
        else if (!taste.IsEmpty && snapshot.TopK(PickMode(taste, random).Centre, AnchorCandidates) is { Count: > 0 } nearest)
        {
            var scores = nearest.All(hit => exclude.Contains(hit.TrackId))
                ? nearest.Select(hit => (double)hit.Score).ToArray()
                : nearest.Select(hit => hit.Score - (exclude.Contains(hit.TrackId) ? AnchorRecencyPenalty : 0)).ToArray();

            var best = scores.Max();
            var weights = scores.Select(score => Math.Exp((score - best) / AnchorTemperature)).ToArray();
            var threshold = random.NextDouble() * weights.Sum();

            anchorRow = nearest[0].Row;
            for (var i = 0; i < weights.Length; i++)
            {
                threshold -= weights[i];
                if (threshold > 0)
                    continue;

                anchorRow = nearest[i].Row;
                break;
            }
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
            Size: request.Limit ?? RecommendationTuning.Exploration.QueueSize,
            Now: now,
            Seed: random.Next(),
            Session: session,
            Mood: moodRanks));

        if (queue.Count == 0)
        {
            logger.LogDebug("Radio found nothing to continue track {SeedTrackId} with", request.SeedTrackId);
            return new RadioBatchDto([], anchorId);
        }

        var tracks = await db.TracksByIdAsync(
            userId, anchorId is { } anchor ? queue.Select(item => item.TrackId).Append(anchor) : queue.Select(item => item.TrackId), ct);

        var anchorTitle = anchorId is { } anchorTrack && tracks.TryGetValue(anchorTrack, out var anchorDto)
            ? anchorDto.Title
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
            .ToList();

        return new RadioBatchDto(result, anchorId);
    }

    // Первый трек радио по настроению: из самых подходящих по настроению — ближе всего ко вкусу
    // (без вкуса — просто по настроению), со случайностью, чтобы каждый запуск начинался по-разному.
    private static int MoodAnchor(
        EmbeddingSnapshot snapshot, float[] moodRanks, TasteModel taste, IReadOnlySet<Guid> exclude, Random random)
    {
        var floor = 1 - QueueBuilder.MoodShare;
        var tasteSimilarities = taste.IsEmpty ? null : taste.SimilaritiesIn(snapshot);

        var candidates = Enumerable.Range(0, snapshot.Count)
            .Where(row => moodRanks[row] >= floor && !exclude.Contains(snapshot.MetaAt(row).TrackId))
            .Select(row => (Row: row, Score: moodRanks[row] + (tasteSimilarities?[row] ?? 0)))
            .OrderByDescending(item => item.Score)
            .Take(AnchorCandidates)
            .ToList();

        return candidates.Count == 0 ? -1 : candidates[random.Next(candidates.Count)].Row;
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

    // Что пользователь делал в последние минуты: дослушанное и лайкнутое тянет очередь к себе,
    // брошенное в начале и отвергнутое — отталкивает. Свежие события весят больше.
    private async Task<float[]?> SessionVectorAsync(Guid userId, EmbeddingSnapshot snapshot, DateTimeOffset now, CancellationToken ct)
    {
        var since = now - SessionWindow;

        var events = await db.PlaybackEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.TrackId != null && e.OccurredAt >= since
                        && (e.Type == PlaybackEventType.TrackCompleted
                            || e.Type == PlaybackEventType.TrackSkipped
                            || e.Type == PlaybackEventType.TrackReplayed
                            || e.Type == PlaybackEventType.TrackLiked
                            || e.Type == PlaybackEventType.TrackUnliked
                            || e.Type == PlaybackEventType.TrackDismissed))
            .Select(e => new { TrackId = e.TrackId!.Value, e.Type, e.OccurredAt, e.ListenedSeconds, e.DurationSeconds })
            .ToListAsync(ct);

        float[]? session = null;

        foreach (var item in events)
        {
            var row = snapshot.RowOf(item.TrackId);
            if (row < 0)
                continue;

            var weight = item.Type switch
            {
                PlaybackEventType.TrackCompleted or PlaybackEventType.TrackReplayed => 1.0,
                PlaybackEventType.TrackLiked => 1.5,
                PlaybackEventType.TrackUnliked => -1.5,
                PlaybackEventType.TrackDismissed => -2.0,
                _ => EventWeights.CompletionRatio(item.ListenedSeconds, item.DurationSeconds) switch
                {
                    < 0.20 => -1.0,
                    < 0.50 => -0.3,
                    _ => 0.3,
                },
            };

            var age = Math.Max(0, (now - item.OccurredAt).TotalMinutes);
            weight *= Math.Pow(0.5, age / SessionHalfLifeMinutes);

            session ??= new float[snapshot.Dimension];
            TensorPrimitives.MultiplyAdd(snapshot.Vector(row), (float)weight, session, session);
        }

        if (session is not null)
            VectorMath.NormalizeInPlace(session);

        return session;
    }

    private sealed record TransitionRow(Guid ToTrackId, double Weight);
}
