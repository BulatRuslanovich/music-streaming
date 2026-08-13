using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

/// <summary>Итог приёма пачки событий — сколько встало в очередь, а сколько отклонено.</summary>
/// <param name="Accepted">Число событий, успешно поставленных в очередь записи.</param>
/// <param name="Rejected">Число событий, отклонённых как невалидные, отброшенных при переполнении очереди или превысивших лимит на запрос.</param>
public record RecordEventsResultDto(int Accepted, int Rejected);

/// <summary>
/// Проверяет присланную пачку и передаёт её в очередь записи.
///
/// <para>
/// Ничто здесь не обращается к базе. Запрос возвращается сразу, как только события поставлены в
/// очередь, — так телеметрия не оказывается на пути задержки плеера, который пытается включить
/// следующий трек.
/// </para>
/// </summary>
public class EventIngestService(
    EventIngestQueue queue,
    RecommendationRefreshQueue refreshQueue,
    ICurrentUser currentUser,
    TimeProvider clock,
    IOptions<RecommendationOptions> options,
    RecommendationMetrics metrics,
    ILogger<EventIngestService> logger)
{
    /// <summary>
    /// Валидирует и ставит в очередь одну пачку событий от текущего пользователя, а также
    /// помечает его профиль для последующего пересчёта.
    /// </summary>
    /// <param name="request">Пачка сырых событий от клиента.</param>
    /// <returns>Сколько событий принято и сколько отклонено — для ответа клиенту и метрик.</returns>
    public RecordEventsResultDto Accept(RecordEventsRequest request)
    {
        var reported = request.Events;
        if (reported is null || reported.Count == 0)
            return new RecordEventsResultDto(0, 0);

        var now = clock.GetUtcNow();
        var userId = currentUser.Id;
        var limit = Math.Min(reported.Count, options.Value.MaxEventsPerRequest);

        var accepted = 0;
        var rejected = 0;

        for (var index = 0; index < limit; index++)
        {
            var playbackEvent = PlaybackEventFactory.TryCreate(reported[index], userId, now);

            if (playbackEvent is null || !queue.TryEnqueue(playbackEvent))
            {
                rejected++;
                continue;
            }

            accepted++;
        }

        // Всё сверх лимита на запрос считается отклонённым, а не молча забывается, — так клиента,
        // шлющего слишком большие пачки, видно в метриках.
        rejected += reported.Count - limit;

        if (accepted > 0)
            refreshQueue.MarkDirty(userId, now);

        if (rejected > 0)
        {
            metrics.RecordEventsDropped(rejected, "rejected");
            logger.LogDebug("Discarded {Rejected} of {Total} reported events", rejected, reported.Count);
        }

        return new RecordEventsResultDto(accepted, rejected);
    }
}
