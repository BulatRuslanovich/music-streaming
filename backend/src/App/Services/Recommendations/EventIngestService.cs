// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Abstractions;
using App.Dtos;
using App.Recommendations;
using App.Recommendations.Scoring;
using Microsoft.Extensions.Logging;

namespace App.Services.Recommendations;

public record RecordEventsResultDto(int Accepted, int Rejected);

public class EventIngestService(
    EventIngestQueue queue,
    RecommendationRefreshQueue refreshQueue,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<EventIngestService> logger)
{
    public RecordEventsResultDto Accept(RecordEventsRequest request)
    {
        var reported = request.Events;
        if (reported is null || reported.Count == 0)
            return new RecordEventsResultDto(0, 0);

        var now = clock.GetUtcNow();
        var userId = currentUser.Id;
        var limit = Math.Min(reported.Count, RecommendationTuning.Maintenance.MaxEventsPerRequest);

        var accepted = 0;
        var rejected = 0;
        var forceRefresh = false;

        for (var index = 0; index < limit; index++)
        {
            var playbackEvent = PlaybackEventFactory.TryCreate(reported[index], userId, now);

            if (playbackEvent is null || !queue.TryEnqueue(playbackEvent))
            {
                rejected++;
                continue;
            }

            accepted++;
            var ratio = EventWeights.CompletionRatio(
                playbackEvent.ListenedSeconds, playbackEvent.DurationSeconds);
            forceRefresh |= EventWeights.ShouldRefreshRecommendations(playbackEvent.Type, ratio);
        }

        rejected += reported.Count - limit;

        if (accepted > 0)
            refreshQueue.MarkDirty(userId, now, forceRefresh);

        if (rejected > 0)
            logger.LogDebug("Discarded {Rejected} of {Total} reported events", rejected, reported.Count);

        return new RecordEventsResultDto(accepted, rejected);
    }
}
