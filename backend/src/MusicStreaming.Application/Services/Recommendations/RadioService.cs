// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

public class RadioService(
    DjSessionService dj,
    UserSettingsService settings,
    RecommendationMetrics metrics,
    IOptions<RecommendationOptions> options,
    ILogger<RadioService> logger)
{
    public async Task<RadioBatchDto> NextAsync(RadioRequest request, CancellationToken ct = default)
    {
        if (!(await settings.GetAsync(ct)).Autoplay)
            return RadioBatchDto.Empty;

        metrics.RecordRequest("radio");

        var batch = await dj.GenerateAsync(new DjRequest(
            DjMode.Flow,
            DjVariety.Balanced,
            request.SeedTrackId,
            request.Exclude,
            request.Limit ?? options.Value.QueueSize), ct);

        if (batch.Tracks.Count == 0)
            logger.LogDebug("Radio found nothing to continue track {SeedTrackId} with", batch.SeedTrackId);

        return new RadioBatchDto(batch.Tracks, batch.SeedTrackId);
    }
}
