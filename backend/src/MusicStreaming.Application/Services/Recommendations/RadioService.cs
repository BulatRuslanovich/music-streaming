// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

public class RadioService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    FlowQueueService flow,
    TimeProvider clock,
    ILogger<RadioService> logger)
{
    public const int MaxBatchSize = 20;

    public async Task<RadioBatchDto> NextAsync(RadioRequest request, CancellationToken ct = default)
    {
        if (request.Limit is < 1 or > MaxBatchSize)
            throw new ValidationException($"Radio limit must be between 1 and {MaxBatchSize}.");

        var userId = currentUser.Id;
        var queue = await flow.BuildAsync(
            userId,
            request.SeedTrackId,
            request.Exclude ?? [],
            request.Limit ?? RecommendationTuning.Exploration.QueueSize,
            clock.GetUtcNow(),
            ct);

        if (queue.Items.Count == 0)
        {
            logger.LogDebug("Radio found nothing to continue track {SeedTrackId} with", request.SeedTrackId);
            return new RadioBatchDto([], queue.AnchorTrackId);
        }

        var wantedIds = queue.Items.Select(item => item.TrackId);
        if (queue.AnchorTrackId is { } anchorId)
            wantedIds = wantedIds.Append(anchorId);

        var tracks = await db.TracksByIdAsync(userId, wantedIds, ct);

        var anchorTitle = queue.AnchorTrackId is { } anchor && tracks.TryGetValue(anchor, out var seed)
            ? seed.Title
            : null;

        var result = queue.Items
            .Where(item => tracks.ContainsKey(item.TrackId))
            .Select(item => new RecommendedTrackDto(
                tracks[item.TrackId],
                new RecommendationReasonDto(
                    item.Explore ? ReasonKinds.Discovery
                    : anchorTitle is null ? ReasonKinds.MatchesYourTaste
                    : ReasonKinds.SoundsLike,
                    item.Explore ? null : anchorTitle,
                    queue.AnchorTrackId),
                null,
                new QueueSignalsDto(item.Explore)))
            .ToList();

        return new RadioBatchDto(result, queue.AnchorTrackId);
    }
}
