// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;

namespace MusicStreaming.Application.Services.Recommendations;

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

public class RecommendationDiagnosticsService(IApplicationDbContext db, TimeProvider clock)
{
    public async Task<RecommendationStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();

        var totals = await TotalsAsync(now, ct);
        var shelfSizes = await ShelfSizesAsync(ct);

        var runs = await db.RecommendationRuns.AsNoTracking()
            .OrderByDescending(r => r.StartedAt)
            .Take(10)
            .Select(r => new RecommendationRunDto(
                r.Id, r.UserId, r.Trigger.ToString(), r.StartedAt, r.DurationMs,
                r.CandidateCount, r.ShelfCount, r.Status.ToString(), r.Error))
            .ToListAsync(ct);

        return new RecommendationStatsDto(
            totals.EventsStored,
            totals.NewestEvent,
            totals.ProfiledUsers,
            totals.AffinityRows,
            totals.SimilarityRows,
            totals.TracksWithNeighbours,
            totals.CachedShelves,
            totals.StaleShelves,
            totals.Impressions == 0 ? 0 : (double)totals.Clicks / totals.Impressions,
            runs,
            shelfSizes);
    }

    private async Task<DiagnosticsTotalsRow> TotalsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var rows = await db.Set<DiagnosticsTotalsRow>().FromSql(
            $"""
            SELECT (SELECT COUNT(*) FROM playback_events)::bigint          AS events_stored,
                   (SELECT MAX(occurred_at) FROM playback_events)          AS newest_event,
                   (SELECT COUNT(*) FROM user_taste_profiles)::int         AS profiled_users,
                   (SELECT COUNT(*) FROM user_track_affinity)::int         AS affinity_rows,
                   (SELECT COUNT(*) FROM track_similarity)::int            AS similarity_rows,
                   (SELECT COUNT(DISTINCT track_id) FROM track_similarity)::int
                                                                          AS tracks_with_neighbours,
                   (SELECT COUNT(*) FROM recommendation_cache)::int        AS cached_shelves,
                   (SELECT COUNT(*) FROM recommendation_cache
                     WHERE expires_at <= {now})::int                       AS stale_shelves,
                   (SELECT COUNT(*) FROM recommendation_impressions)::int  AS impressions,
                   (SELECT COUNT(*) FROM recommendation_impressions
                     WHERE clicked_at IS NOT NULL)::int                    AS clicks
            """).ToListAsync(ct);

        return rows[0];
    }

    private async Task<List<ShelfSizeDto>> ShelfSizesAsync(CancellationToken ct)
    {
        var rows = await db.Set<ShelfSizeRow>().FromSql(
            $"""
            SELECT shelf_key                                   AS shelf_key,
                   COUNT(*)::int                               AS users,
                   AVG(jsonb_array_length(payload))::float8    AS average_items
            FROM recommendation_cache
            GROUP BY shelf_key
            ORDER BY 2 DESC
            """).ToListAsync(ct);

        return [.. rows.Select(row => new ShelfSizeDto(row.ShelfKey, row.Users, row.AverageItems))];
    }
}

public class DiagnosticsTotalsRow
{
    public long EventsStored { get; set; }
    public DateTimeOffset? NewestEvent { get; set; }
    public int ProfiledUsers { get; set; }
    public int AffinityRows { get; set; }
    public int SimilarityRows { get; set; }
    public int TracksWithNeighbours { get; set; }
    public int CachedShelves { get; set; }
    public int StaleShelves { get; set; }
    public int Impressions { get; set; }
    public int Clicks { get; set; }
}

public class ShelfSizeRow
{
    public string ShelfKey { get; set; } = string.Empty;
    public int Users { get; set; }
    public double AverageItems { get; set; }
}
