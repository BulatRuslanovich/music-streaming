using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Domain.Entities.Recommendations;
using MusicStreaming.Infrastructure.Persistence;

namespace MusicStreaming.Infrastructure.Recommendations;

/// <summary>
/// Пишет поставленные в очередь поведенческие события в журнал.
///
/// <para>
/// Единственный читатель — так задумано. Именно прогон всех вставок через одного воркера делает
/// последовательность поступления в <see cref="PlaybackEvent.Sequence"/> безопасной отметкой для
/// роллапа: при конкурентных писателях меньший номер мог бы стать видимым уже после обработки
/// большего, и такие события были бы потеряны.
/// </para>
/// </summary>
public class EventIngestWorker(
    IServiceScopeFactory scopeFactory,
    EventIngestQueue queue,
    RecommendationMetrics metrics,
    ILogger<EventIngestWorker> logger) : BackgroundService
{
    private const int MaxBatchSize = 500;

    /// <summary>
    /// Основной цикл воркера: непрерывно забирает батчи из очереди и пишет их в базу, пока хост не
    /// остановлен. Ошибка одной пачки не прерывает цикл — обработка продолжается со следующей.
    /// </summary>
    /// <param name="stoppingToken">Токен остановки хоста.</param>
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
                // Телеметрия никогда не стоит того, чтобы ронять воркера; следующая пачка может
                // оказаться исправной.
                logger.LogError(ex, "Writing a batch of playback events failed");
            }
        }
    }

    /// <summary>Создаёт собственную область DI (воркер живёт дольше любого запроса, поэтому не может делить DbContext с ним), фильтрует и вставляет батч.</summary>
    /// <param name="batch">Батч событий, готовых к записи.</param>
    /// <param name="ct">Токен отмены.</param>
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
    }

    /// <summary>
    /// Отбрасывает события, чей трек исчез между отправкой и записью. Иначе удалённый трек завалил
    /// бы всю пачку по внешнему ключу, потянув за собой все никак не связанные с ним события.
    /// </summary>
    /// <param name="db">Контекст базы данных для проверки существования треков.</param>
    /// <param name="batch">Исходный батч событий.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Тот же батч, если все треки на месте; иначе — подмножество без событий на удалённые треки.</returns>
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
