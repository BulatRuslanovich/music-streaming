// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Abstractions;
using App.Common;
using App.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using App.Recommendations.Embeddings;
using App.Recommendations.Home;

namespace App.Recommendations.Radio;

public class RadioService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    EmbeddingIndex index,
    TasteVectorReader tasteVectors,
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

        var taste = await tasteVectors.CurrentAsync(userId, snapshot, ct);

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
        else if (taste.IsReady && random.NextDouble() < AnchorRandomChance && snapshot.Count > 8)
        {
            anchorRow = random.Next(snapshot.Count);
        }
        else if (taste.IsReady && snapshot.TopK(taste.Query, AnchorCandidates) is { Count: > 0 } nearest)
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

            transitions = await db.TrackTransitions.AsNoTracking()
                .Where(transition => transition.FromTrackId == from && transition.Weight >= 1)
                .OrderByDescending(transition => transition.Weight)
                .Take(200)
                .ToDictionaryAsync(transition => transition.ToTrackId, transition => transition.Weight, ct);
        }

        var queue = QueueBuilder.Build(snapshot, new QueueRequest(
            CurrentRow: anchorRow,
            Taste: taste.Query,
            Exclude: exclude,
            ExploreRatio: VectorMaturity.EffectiveExplore(
                RecommendationTuning.Exploration.QueueRatio, RecommendationTuning.Exploration.QueueDiscoverRatio, taste.Maturity),
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
}
