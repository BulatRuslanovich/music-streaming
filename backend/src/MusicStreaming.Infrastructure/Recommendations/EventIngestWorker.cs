// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Infrastructure.Persistence;

namespace MusicStreaming.Infrastructure.Recommendations;

public class EventIngestWorker(
    IServiceScopeFactory scopeFactory,
    EventIngestQueue queue,
    ILogger<EventIngestWorker> logger) : BackgroundService
{
    private const int MaxBatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var batch = await queue.ReadBatchAsync(MaxBatchSize, stoppingToken);
                if (batch.Count == 0)
                    continue;

                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // События о треке, который успели удалить, пока они шли до базы, отбрасываются.
                var referenced = batch.Where(e => e.TrackId is not null).Select(e => e.TrackId!.Value).Distinct().ToList();
                HashSet<Guid> existing = referenced.Count == 0
                    ? []
                    : [.. await db.Tracks.AsNoTracking().Where(t => referenced.Contains(t.Id)).Select(t => t.Id).ToListAsync(stoppingToken)];

                var writable = batch.Where(e => e.TrackId is null || existing.Contains(e.TrackId.Value)).ToList();

                if (writable.Count < batch.Count)
                    logger.LogDebug("Dropped {Count} events that referenced a deleted track", batch.Count - writable.Count);

                if (writable.Count == 0)
                    continue;

                db.PlaybackEvents.AddRange(writable);
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Writing a batch of playback events failed");
            }
        }
    }
}
