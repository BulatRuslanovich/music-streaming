using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Application.Services.Integrations;
using MusicStreaming.Domain.Entities.Recommendations;
using MusicStreaming.Infrastructure.Persistence;

namespace MusicStreaming.Infrastructure.Recommendations;

public class EventIngestWorker(
    IServiceScopeFactory scopeFactory,
    EventIngestQueue queue,
    RecommendationMetrics metrics,
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

                await WriteAsync(batch, stoppingToken);
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

    private async Task WriteAsync(List<PlaybackEvent> batch, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var writable = await FilterToExistingTracksAsync(db, batch, ct);
        if (writable.Count == 0)
            return;

        db.PlaybackEvents.AddRange(writable);
        await db.SaveChangesAsync(ct);

        metrics.RecordEventsIngested(writable.Count);

        try
        {
            await scope.ServiceProvider.GetRequiredService<ScrobbleQueueing>().QueueAsync(writable, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Queueing outbound scrobbles failed");
        }
    }

    private async Task<List<PlaybackEvent>> FilterToExistingTracksAsync(
        ApplicationDbContext db, List<PlaybackEvent> batch, CancellationToken ct)
    {
        var referenced = batch
            .Where(e => e.TrackId is not null)
            .Select(e => e.TrackId!.Value)
            .Distinct()
            .ToList();

        if (referenced.Count == 0)
            return batch;

        var existing = await db.Tracks.AsNoTracking()
            .Where(t => referenced.Contains(t.Id))
            .Select(t => t.Id)
            .ToListAsync(ct);

        if (existing.Count == referenced.Count)
            return batch;

        var known = existing.ToHashSet();
        var writable = batch.Where(e => e.TrackId is null || known.Contains(e.TrackId.Value)).ToList();

        var dropped = batch.Count - writable.Count;
        if (dropped > 0)
        {
            metrics.RecordEventsDropped(dropped, "missing_track");
            logger.LogDebug("Dropped {Count} events that referenced a deleted track", dropped);
        }

        return writable;
    }
}
