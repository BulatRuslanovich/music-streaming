// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Services.Integrations;

namespace MusicStreaming.Infrastructure.Integrations;

public class LibraryEnrichmentWorker(
    IServiceScopeFactory scopeFactory,
    LibraryEnrichmentQueue queue,
    ILogger<LibraryEnrichmentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                foreach (var artistId in request.NewArtistIds.Distinct())
                {
                    await RunAsync(
                        (enrichment, token) => enrichment.EnrichArtistAsync(artistId, token),
                        $"Artist image enrichment for {artistId}",
                        stoppingToken);

                    await DelayAsync(1000, stoppingToken);
                }

                await RunAsync(
                    (enrichment, token) => enrichment.EnrichLyricsAsync(request.TrackId, token),
                    $"Lyrics enrichment for track {request.TrackId}",
                    stoppingToken);

                await DelayAsync(500, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Library enrichment failed for track {TrackId}", request.TrackId);
            }
            finally
            {
                queue.MarkFinished(request);
            }
        }
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
