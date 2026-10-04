// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Infrastructure.Persistence;
using App.Recommendations;

namespace Infrastructure.Recommendations;

public class LibraryMaintenance(
    ApplicationDbContext db,
    FileSystemMusicStorage storage,
    FileSystemImageStorage images,
    TimeProvider clock,
    ILogger<LibraryMaintenance> logger)
{
    public async Task RefreshTrackStatsAsync(CancellationToken ct = default)
    {
        var affected = await db.Database.ExecuteSqlRawAsync(
            """
            WITH recent AS (
                SELECT track_id, COUNT(*) AS plays
                FROM playback_events
                WHERE track_id IS NOT NULL
                  AND type IN (3, 4)
                  AND occurred_at >= now() - make_interval(days => 30)
                GROUP BY track_id
            ),
            abandoned AS (
                SELECT track_id, COUNT(*) AS drops
                FROM playback_events
                WHERE track_id IS NOT NULL
                  AND type = 4
                  AND duration_seconds > 0
                  AND occurred_at >= now() - make_interval(days => 30)
                  AND listened_seconds::double precision / duration_seconds < 0.2
                GROUP BY track_id
            ),
            rollup AS (
                SELECT
                    a.track_id,
                    SUM(a.play_count) AS play_count
                FROM user_track_affinity a
                GROUP BY a.track_id
            )
            INSERT INTO track_stats (
                track_id, play_count, popularity_score, skipped_early_count)
            SELECT
                t.id,
                COALESCE(r.play_count, 0),
                (COALESCE(recent.plays, 0) * 2 + COALESCE(r.play_count, 0))::double precision
                    / ((COALESCE(recent.plays, 0) * 2 + COALESCE(r.play_count, 0)) + 10),
                COALESCE(abandoned.drops, 0)
            FROM tracks t
            LEFT JOIN rollup r ON r.track_id = t.id
            LEFT JOIN recent ON recent.track_id = t.id
            LEFT JOIN abandoned ON abandoned.track_id = t.id
            ON CONFLICT (track_id) DO UPDATE SET
                play_count = EXCLUDED.play_count,
                popularity_score = EXCLUDED.popularity_score,
                skipped_early_count = EXCLUDED.skipped_early_count
            WHERE track_stats.play_count IS DISTINCT FROM EXCLUDED.play_count
               OR track_stats.popularity_score IS DISTINCT FROM EXCLUDED.popularity_score
               OR track_stats.skipped_early_count IS DISTINCT FROM EXCLUDED.skipped_early_count;
            """, ct);
        logger.LogDebug("Refreshed statistics for {Count} tracks", affected);
    }

    public async Task PruneAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var eventCutoff = now.AddDays(-RecommendationTuning.Maintenance.EventRetentionDays);

        var events = await db.PlaybackEvents.Where(e => e.OccurredAt < eventCutoff).ExecuteDeleteAsync(ct);

        if (events > 0)
            logger.LogInformation("Pruned {Events} playback events", events);

        var coverPaths = await db.Albums
            .Where(a => !a.Tracks.Any() && a.CoverPath != null)
            .Select(a => a.CoverPath!)
            .ToListAsync(ct);

        var albums = await db.Albums.Where(a => !a.Tracks.Any()).ExecuteDeleteAsync(ct);

        var imagePaths = await db.Artists
            .Where(a => !a.Tracks.Any() && !a.Albums.Any() && !a.TrackCredits.Any() && a.ImagePath != null)
            .Select(a => a.ImagePath!)
            .ToListAsync(ct);

        var artists = await db.Artists
            .Where(a => !a.Tracks.Any() && !a.Albums.Any() && !a.TrackCredits.Any())
            .ExecuteDeleteAsync(ct);

        var genres = await db.Genres.Where(g => !g.Tracks.Any()).ExecuteDeleteAsync(ct);

        foreach (var path in coverPaths)
            images.DeleteCover(path);

        foreach (var path in imagePaths)
            storage.Delete(path);

        if (albums + artists + genres > 0)
        {
            logger.LogInformation(
                "Pruned {Albums} orphaned albums, {Artists} artists and {Genres} genres",
                albums, artists, genres);
        }
    }
}
