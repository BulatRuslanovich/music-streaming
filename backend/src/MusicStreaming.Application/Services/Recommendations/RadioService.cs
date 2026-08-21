// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

public class RadioService(
    DjSessionService dj,
    UserSettingsService settings,
    RecommendationMetrics metrics,
    ILogger<RadioService> logger)
{
    public const int BatchSize = 5;

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
            request.Limit ?? BatchSize), ct);

        if (batch.Tracks.Count == 0)
        {
            logger.LogDebug("Radio found nothing to continue track {SeedTrackId} with", batch.SeedTrackId);
            return new RadioBatchDto([], batch.SeedTrackId);
        }

        return new RadioBatchDto(batch.Tracks, batch.SeedTrackId);
    }
}
