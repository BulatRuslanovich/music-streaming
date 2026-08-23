// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Services.Integrations;

namespace MusicStreaming.Infrastructure.Integrations;

public class LibraryEnrichmentWorker(
    IServiceScopeFactory scopeFactory,
    LibraryEnrichmentQueue queue,
    IOptions<AudioDbOptions> audioDbOptions,
    IOptions<LrclibOptions> lrclibOptions,
    IOptions<LibraryEnrichmentOptions> enrichmentOptions,
    ILogger<LibraryEnrichmentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!enrichmentOptions.Value.Enabled)
            return;

        await queue.ConsumeAsync(
            ProcessAsync,
            (request, ex) =>
                logger.LogError(ex, "Library enrichment failed for track {TrackId}", request.TrackId),
            stoppingToken);
    }

    private async Task ProcessAsync(LibraryEnrichmentRequest request, CancellationToken ct)
    {
        foreach (var artistId in request.NewArtistIds.Distinct())
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var enrichment = scope.ServiceProvider.GetRequiredService<LibraryEnrichment>();
                var result = await enrichment.EnrichArtistAsync(artistId, ct);
                logger.LogInformation(
                    "Artist image enrichment for {ArtistId} finished with {Status}", artistId, result.Status);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Artist image enrichment failed for {ArtistId}", artistId);
            }

            await DelayAsync(audioDbOptions.Value.RequestDelayMs, ct);
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var enrichment = scope.ServiceProvider.GetRequiredService<LibraryEnrichment>();
            var result = await enrichment.EnrichLyricsAsync(request.TrackId, ct);
            logger.LogInformation(
                "Lyrics enrichment for track {TrackId} finished with {Status}", request.TrackId, result.Status);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Lyrics enrichment failed for track {TrackId}", request.TrackId);
        }

        await DelayAsync(lrclibOptions.Value.RequestDelayMs, ct);
    }

    private static Task DelayAsync(int milliseconds, CancellationToken ct) =>
        milliseconds > 0 ? Task.Delay(milliseconds, ct) : Task.CompletedTask;
}
