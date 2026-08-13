using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;

namespace MusicStreaming.Application.Services.Recommendations;

/// <summary>Как чувствует себя движок — в тех терминах, в которых спросил бы оператор.</summary>
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
