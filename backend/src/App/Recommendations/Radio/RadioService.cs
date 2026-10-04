// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Infrastructure.Persistence;
using App.Abstractions;
using App.Common;
using App.Dtos;
using Microsoft.EntityFrameworkCore;
using Domain.Entities.Recommendations;
using App.Recommendations.Embeddings;
using App.Recommendations.Home;

namespace App.Recommendations.Radio;

public class RadioService(
    ApplicationDbContext db,
    ICurrentUser currentUser,
    EmbeddingIndex index,
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

    public async Task<RadioBatchDto> NextAsync(RadioRequest request, CancellationToken ct = default)
    {
        if (request.Limit is < 1 or > MaxBatchSize)
            throw new ValidationException($"Radio limit must be between 1 and {MaxBatchSize}.");

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

        var clientExclude = request.Exclude ?? [];
        var exclude = new HashSet<Guid>(clientExclude);
        foreach (var trackId in recent.Concat(clientExclude))
            exclude.UnionWith(snapshot.CloneIds(trackId));

        var random = new Random(Explorer.SeedFor(userId, "radio", now) ^ (int)(now.Ticks & 0xFFFF));

        // Якорь: заданный трек, иначе изредка случайный, иначе сэмпл (softmax) из ближайших к вкусу,
        // где недавно слушанные приглушены.
        var anchorRow = -1;

        if (request.SeedTrackId is { } seed && snapshot.RowOf(seed) is var seeded and >= 0)
        {
            anchorRow = seeded;
        }
        else if (taste.Length > 0 && random.NextDouble() < AnchorRandomChance && snapshot.Count > 8)
        {
            anchorRow = random.Next(snapshot.Count);
        }
        else if (taste.Length > 0 && snapshot.TopK(taste, AnchorCandidates) is { Count: > 0 } nearest)
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
            Seed: random.Next()));

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

    private sealed record TransitionRow(Guid ToTrackId, double Weight);
}
