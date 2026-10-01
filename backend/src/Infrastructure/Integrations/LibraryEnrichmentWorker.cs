// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using App.Services.Integrations;
using Infrastructure.Persistence;

namespace Infrastructure.Integrations;

public class LibraryEnrichmentWorker(
    IServiceScopeFactory scopeFactory,
    LibraryEnrichmentQueue queue,
    ILogger<LibraryEnrichmentWorker> logger) : BackgroundService
{
    private const int ArtistLookupPauseMs = 2000;
    private const int LyricsLookupPauseMs = 500;
    private const int BackfillChunk = 20;
    private static readonly TimeSpan BackfillDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Run(async () =>
        {
            try
            {
                await Task.Delay(BackfillDelay, stoppingToken);

                using var scope = scopeFactory.CreateScope();
                var artistIds = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Artists
                    .AsNoTracking()
                    .Where(artist => artist.ImagePath == null)
                    .Select(artist => artist.Id)
                    .ToListAsync(stoppingToken);

                foreach (var chunk in artistIds.Chunk(BackfillChunk))
                    queue.TryEnqueue(new LibraryEnrichmentRequest(null, chunk));

                logger.LogInformation("Queued {Count} artists without a photo for a lookup", artistIds.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Queueing artists without a photo failed");
            }
        }, stoppingToken);

        await foreach (var request in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                foreach (var artistId in request.ArtistIds.Distinct())
                {
                    await RunAsync(
                        (enrichment, token) => enrichment.EnrichArtistAsync(artistId, token),
                        $"Artist image enrichment for {artistId}",
                        stoppingToken);

                    await Task.Delay(ArtistLookupPauseMs, stoppingToken);
                }

                if (request.TrackId is not { } trackId) continue;
                {
                    await RunAsync(
                        (enrichment, token) => enrichment.EnrichLyricsAsync(trackId, token),
                        $"Lyrics enrichment for track {trackId}",
                        stoppingToken);

                    await Task.Delay(LyricsLookupPauseMs, stoppingToken);
                }
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
}
