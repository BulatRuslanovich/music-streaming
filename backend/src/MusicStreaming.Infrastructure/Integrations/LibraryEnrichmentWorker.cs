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
    IOptions<TagEnrichmentOptions> tagOptions,
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
            await RunAsync(
                (enrichment, token) => enrichment.EnrichArtistAsync(artistId, token),
                $"Artist image enrichment for {artistId}",
                ct);

            await DelayAsync(audioDbOptions.Value.RequestDelayMs, ct);

            await RunAsync(
                (enrichment, token) => enrichment.EnrichArtistTagsAsync(artistId, token),
                $"Artist tag enrichment for {artistId}",
                ct);

            await DelayAsync(tagOptions.Value.RequestDelayMs, ct);
        }

        await RunAsync(
            (enrichment, token) => enrichment.EnrichLyricsAsync(request.TrackId, token),
            $"Lyrics enrichment for track {request.TrackId}",
            ct);

        await DelayAsync(lrclibOptions.Value.RequestDelayMs, ct);

        await RunAsync(
            (enrichment, token) => enrichment.EnrichTrackTagsAsync(request.TrackId, token),
            $"Track tag enrichment for {request.TrackId}",
            ct);

        await DelayAsync(tagOptions.Value.RequestDelayMs, ct);
    }

    private async Task RunAsync(
        Func<LibraryEnrichment, CancellationToken, Task<EnrichmentResult>> step,
        string description,
        CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var enrichment = scope.ServiceProvider.GetRequiredService<LibraryEnrichment>();
            var result = await step(enrichment, ct);
            logger.LogInformation("{Step} finished with {Status}", description, result.Status);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "{Step} failed", description);
        }
    }

    private static Task DelayAsync(int milliseconds, CancellationToken ct) =>
        milliseconds > 0 ? Task.Delay(milliseconds, ct) : Task.CompletedTask;
}
