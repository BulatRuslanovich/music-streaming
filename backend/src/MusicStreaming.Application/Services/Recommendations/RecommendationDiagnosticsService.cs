using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;

namespace MusicStreaming.Application.Services.Recommendations;

/// <summary>Как чувствует себя движок — в тех терминах, в которых спросил бы оператор.</summary>
/// <param name="EventsStored">Всего событий воспроизведения в журнале.</param>
/// <param name="NewestEvent">Момент самого свежего записанного события — большое отставание означает проблему в приёме или воркере записи.</param>
/// <param name="ProfiledUsers">Число пользователей, у которых есть построенный профиль вкуса.</param>
/// <param name="AffinityRows">Суммарное число строк аффинити (треки+исполнители+жанры) — грубая мера объёма накопленного сигнала.</param>
/// <param name="SimilarityRows">Число пар похожести в таблице item-item.</param>
/// <param name="TracksWithNeighbours">Сколько различных треков имеют хотя бы одного похожего соседа — показывает покрытие таблицы похожести.</param>
/// <param name="CachedShelves">Число предрассчитанных и ещё не устаревших наборов полок в кэше.</param>
/// <param name="StaleShelves">Число закэшированных наборов полок, которые уже устарели, но ещё не перестроены.</param>
/// <param name="ImpressionClickRate">Доля показанных рекомендаций, по которым кликнули — CTR движка.</param>
/// <param name="RecentRuns">Последние проходы генерации полок, для отладки конкретных сбоев или задержек.</param>
/// <param name="ShelfSizes">Средний размер полки по каждому ключу полки.</param>
public record RecommendationStatsDto(
    long EventsStored,
    DateTimeOffset? NewestEvent,
    int ProfiledUsers,
    int AffinityRows,
    int SimilarityRows,
    int TracksWithNeighbours,
    int CachedShelves,
    int StaleShelves,
    double ImpressionClickRate,
    IReadOnlyList<RecommendationRunDto> RecentRuns,
    IReadOnlyList<ShelfSizeDto> ShelfSizes);

/// <summary>Один проход построения полок — для какого пользователя, чем вызван, сколько занял и чем закончился.</summary>
/// <param name="Id">Идентификатор прохода.</param>
/// <param name="UserId">Пользователь, для которого строились полки; <c>null</c> для фоновых проходов не для конкретного человека.</param>
/// <param name="Trigger">Что вызвало проход — например, дебаунс после активности или плановая перестройка.</param>
/// <param name="StartedAt">Момент начала прохода.</param>
/// <param name="DurationMs">Сколько занял проход, в миллисекундах.</param>
/// <param name="CandidateCount">Сколько кандидатов было рассмотрено.</param>
/// <param name="ShelfCount">Сколько полок было построено.</param>
/// <param name="Status">Итоговый статус прохода.</param>
/// <param name="Error">Сообщение об ошибке, если проход завершился неудачно.</param>
public record RecommendationRunDto(
    Guid Id,
    Guid? UserId,
    string Trigger,
    DateTimeOffset StartedAt,
    int DurationMs,
    int CandidateCount,
    int ShelfCount,
    string Status,
    string? Error);

/// <summary>Сводка по одному ключу полки — сколько пользователей её видели и насколько она обычно заполнена.</summary>
public record ShelfSizeDto(string ShelfKey, int Users, double AverageItems);

/// <summary>
/// Диагностика только на чтение, для администраторов.
///
/// <para>
/// Метрики говорят, что генерация медленная; это говорит почему — сколько сигнала накоплено,
/// построена ли таблица похожести, какие полки получаются и кликает ли по ним кто-нибудь. Это
/// первое, куда стоит посмотреть, когда полка пропала или выглядит неправильно.
/// </para>
/// </summary>
public class RecommendationDiagnosticsService(IApplicationDbContext db, TimeProvider clock)
{
    private const int RecentRunCount = 10;

    /// <summary>Собирает моментальный снимок здоровья движка рекомендаций из нескольких агрегирующих запросов.</summary>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Сводка для админ-панели: объём накопленных данных, состояние кэша полок и вовлечённость пользователей.</returns>
    public async Task<RecommendationStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();

        var runs = await db.RecommendationRuns.AsNoTracking()
            .OrderByDescending(r => r.StartedAt)
            .Take(RecentRunCount)
            .Select(r => new RecommendationRunDto(
                r.Id, r.UserId, r.Trigger.ToString(), r.StartedAt, r.DurationMs,
                r.CandidateCount, r.ShelfCount, r.Status.ToString(), r.Error))
            .ToListAsync(ct);

        var shelfSizes = await db.RecommendationCache.AsNoTracking()
            .GroupBy(c => c.ShelfKey)
            .Select(g => new { ShelfKey = g.Key, Users = g.Count() })
            .OrderByDescending(x => x.Users)
            .ToListAsync(ct);

        // Длину payload видно только после материализации: это jsonb-колонка за конвертером
        // значений, поэтому считать её элементы приходится уже на загруженных строках.
        var payloadSizes = await db.RecommendationCache.AsNoTracking()
            .Select(c => new { c.ShelfKey, c.Payload })
            .ToListAsync(ct);

        var averageItems = payloadSizes
            .GroupBy(row => row.ShelfKey)
            .ToDictionary(g => g.Key, g => g.Average(row => (double)row.Payload.Count));

        var impressions = await db.RecommendationImpressions.AsNoTracking().CountAsync(ct);
        var clicks = await db.RecommendationImpressions.AsNoTracking()
            .CountAsync(i => i.ClickedAt != null, ct);

        return new RecommendationStatsDto(
            await db.PlaybackEvents.AsNoTracking().LongCountAsync(ct),
            await db.PlaybackEvents.AsNoTracking().MaxAsync(e => (DateTimeOffset?)e.OccurredAt, ct),
            await db.UserTasteProfiles.AsNoTracking().CountAsync(ct),
            await db.UserTrackAffinities.AsNoTracking().CountAsync(ct),
            await db.TrackSimilarities.AsNoTracking().CountAsync(ct),
            await db.TrackSimilarities.AsNoTracking().Select(s => s.TrackId).Distinct().CountAsync(ct),
            await db.RecommendationCache.AsNoTracking().CountAsync(ct),
            await db.RecommendationCache.AsNoTracking().CountAsync(c => c.ExpiresAt <= now, ct),
            impressions == 0 ? 0 : (double)clicks / impressions,
            runs,
            shelfSizes
                .Select(x => new ShelfSizeDto(x.ShelfKey, x.Users, averageItems.GetValueOrDefault(x.ShelfKey)))
                .ToList());
    }
}
