using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Infrastructure.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace MusicStreaming.Infrastructure.Recommendations;

public class SimilarityMaintenance(
    ApplicationDbContext db,
    IMusicStorage storage,
    IOptions<RecommendationOptions> options,
    ILogger<SimilarityMaintenance> logger)
{
    private const int CoOccurrenceWindowSeconds = 1800;
    private const int MaxCuratedPlaylistSize = 100;
    private const int GenreCoreSize = 60;
    private const double MinimumStoredScore = 0.05;
    private RecommendationOptions Options => options.Value;

    public async Task RefreshTrackStatsAsync(CancellationToken ct = default)
    {
        const string sql = """
            WITH recent AS (
                SELECT track_id, COUNT(*) AS plays
                FROM playback_events
                WHERE track_id IS NOT NULL
                  -- TrackCompleted, TrackSkipped: the events that end a play attempt.
                  AND type IN (3, 4)
                  AND occurred_at >= now() - make_interval(days => 30)
                GROUP BY track_id
            ),
            rollup AS (
                SELECT
                    a.track_id,
                    SUM(a.play_count)                                        AS play_count,
                    SUM(a.skip_count)                                        AS skip_count,
                    COUNT(*) FILTER (WHERE a.play_count > 0)                 AS listeners,
                    SUM(a.completion_sum)                                    AS completion_sum,
                    SUM(a.completion_samples)                                AS completion_samples,
                    MAX(a.last_played_at)                                    AS last_played_at
                FROM user_track_affinity a
                GROUP BY a.track_id
            )
            INSERT INTO track_stats (
                track_id, play_count, play_count30d, skip_count, distinct_listeners,
                completion_rate, skip_rate, popularity_score, last_played_at, computed_at)
            SELECT
                t.id,
                COALESCE(r.play_count, 0),
                COALESCE(recent.plays, 0),
                COALESCE(r.skip_count, 0),
                COALESCE(r.listeners, 0),
                CASE WHEN COALESCE(r.completion_samples, 0) > 0
                     THEN r.completion_sum / r.completion_samples ELSE 0 END,
                CASE WHEN COALESCE(r.play_count, 0) > 0
                     THEN r.skip_count::double precision / r.play_count ELSE 0 END,
                -- Volume squashed into [0, 1); recent plays count double so that popularity
                -- tracks what the library listens to now, not what it listened to a year ago.
                (COALESCE(recent.plays, 0) * 2 + COALESCE(r.play_count, 0))::double precision
                    / ((COALESCE(recent.plays, 0) * 2 + COALESCE(r.play_count, 0)) + 10),
                r.last_played_at,
                now()
            FROM tracks t
            LEFT JOIN rollup r ON r.track_id = t.id
            LEFT JOIN recent ON recent.track_id = t.id
            ON CONFLICT (track_id) DO UPDATE SET
                play_count = EXCLUDED.play_count,
                play_count30d = EXCLUDED.play_count30d,
                skip_count = EXCLUDED.skip_count,
                distinct_listeners = EXCLUDED.distinct_listeners,
                completion_rate = EXCLUDED.completion_rate,
                skip_rate = EXCLUDED.skip_rate,
                popularity_score = EXCLUDED.popularity_score,
                last_played_at = EXCLUDED.last_played_at,
                computed_at = EXCLUDED.computed_at;
            """;

        var affected = await db.Database.ExecuteSqlRawAsync(sql, ct);
        logger.LogDebug("Refreshed statistics for {Count} tracks", affected);
    }

    public async Task RefreshSimilarityAsync(CancellationToken ct = default)
    {
        const string sql = """
            WITH artist_counts AS (
                SELECT track_id, COUNT(*) AS credits
                FROM track_artists
                GROUP BY track_id
            ),
            shared_artists AS (
                SELECT ta1.track_id AS a, ta2.track_id AS b, COUNT(*) AS shared
                FROM track_artists ta1
                JOIN track_artists ta2
                  ON ta2.artist_id = ta1.artist_id
                 AND ta2.track_id > ta1.track_id
                GROUP BY 1, 2
            ),
            album_pairs AS (
                SELECT t1.id AS a, t2.id AS b
                FROM tracks t1
                JOIN tracks t2 ON t2.album_id = t1.album_id AND t2.id > t1.id
                WHERE t1.album_id IS NOT NULL
            ),
            genre_core AS (
                SELECT id, genre_id
                FROM (
                    SELECT t.id, t.genre_id,
                           ROW_NUMBER() OVER (
                               PARTITION BY t.genre_id
                               ORDER BY COALESCE(s.popularity_score, 0) DESC, t.created_at DESC) AS rank
                    FROM tracks t
                    LEFT JOIN track_stats s ON s.track_id = t.id
                    WHERE t.genre_id IS NOT NULL
                ) ranked
                WHERE rank <= @genre_core
            ),
            genre_pairs AS (
                SELECT g1.id AS a, g2.id AS b
                FROM genre_core g1
                JOIN genre_core g2 ON g2.genre_id = g1.genre_id AND g2.id > g1.id
            ),
            session_plays AS (
                SELECT DISTINCT ON (session_id, track_id)
                       session_id, track_id, occurred_at
                FROM playback_events
                WHERE track_id IS NOT NULL
                  AND session_id <> '00000000-0000-0000-0000-000000000000'::uuid
                  -- TrackStarted, TrackCompleted, TrackSkipped: a track was actually put on.
                  AND type IN (1, 3, 4)
                ORDER BY session_id, track_id, occurred_at
            ),
            session_cooc AS (
                SELECT p1.track_id AS a, p2.track_id AS b, COUNT(DISTINCT p1.session_id) AS support
                FROM session_plays p1
                JOIN session_plays p2
                  ON p2.session_id = p1.session_id
                 AND p2.track_id > p1.track_id
                 AND abs(extract(epoch FROM (p2.occurred_at - p1.occurred_at))) <= @window
                GROUP BY 1, 2
            ),
            curated_playlists AS (
                SELECT playlist_id
                FROM playlist_tracks
                GROUP BY playlist_id
                HAVING COUNT(*) <= @max_playlist
            ),
            playlist_cooc AS (
                SELECT pt1.track_id AS a, pt2.track_id AS b, COUNT(DISTINCT pt1.playlist_id) AS support
                FROM playlist_tracks pt1
                JOIN playlist_tracks pt2
                  ON pt2.playlist_id = pt1.playlist_id
                 AND pt2.track_id > pt1.track_id
                JOIN curated_playlists c ON c.playlist_id = pt1.playlist_id
                GROUP BY 1, 2
            ),
            track_contexts AS (
                SELECT track_id, SUM(contexts) AS contexts
                FROM (
                    SELECT track_id, COUNT(DISTINCT session_id) AS contexts
                    FROM session_plays GROUP BY track_id
                    UNION ALL
                    SELECT pt.track_id, COUNT(DISTINCT pt.playlist_id)
                    FROM playlist_tracks pt
                    JOIN curated_playlists c ON c.playlist_id = pt.playlist_id
                    GROUP BY pt.track_id
                ) parts
                GROUP BY track_id
            ),
            candidates AS (
                SELECT a, b, SUM(support) AS support
                FROM (
                    SELECT a, b, 0 AS support FROM shared_artists
                    UNION ALL SELECT a, b, 0 FROM album_pairs
                    UNION ALL SELECT a, b, 0 FROM genre_pairs
                    UNION ALL SELECT a, b, support FROM session_cooc
                    UNION ALL SELECT a, b, support FROM playlist_cooc
                ) all_pairs
                GROUP BY a, b
            ),
            scored AS (
                SELECT
                    c.a,
                    c.b,
                    c.support::int AS support,
                    (@w_artist * COALESCE(sa.shared::double precision
                        / NULLIF(ac1.credits + ac2.credits - sa.shared, 0), 0)
                     + @w_album * CASE WHEN t1.album_id IS NOT NULL AND t1.album_id = t2.album_id
                                       THEN 1 ELSE 0 END
                     + @w_genre * CASE WHEN t1.genre_id IS NOT NULL AND t1.genre_id = t2.genre_id
                                       THEN 1 ELSE 0 END
                     + @w_year * CASE WHEN t1.year IS NULL OR t2.year IS NULL THEN 0
                                      ELSE exp(-abs(t1.year - t2.year) / 8.0) END
                     + @w_duration * exp(
                         -abs(t1.duration_seconds - t2.duration_seconds) / 120.0)
                    ) AS content_score,
                    CASE WHEN c.support > 0
                         THEN LEAST(1.0, c.support::double precision
                                  / NULLIF(sqrt(GREATEST(tc1.contexts, 1)::double precision
                                              * GREATEST(tc2.contexts, 1)), 0))
                              * (c.support::double precision / (c.support + @shrinkage))
                         ELSE 0 END AS collab_score
                FROM candidates c
                JOIN tracks t1 ON t1.id = c.a
                JOIN tracks t2 ON t2.id = c.b
                LEFT JOIN shared_artists sa ON sa.a = c.a AND sa.b = c.b
                LEFT JOIN artist_counts ac1 ON ac1.track_id = c.a
                LEFT JOIN artist_counts ac2 ON ac2.track_id = c.b
                LEFT JOIN track_contexts tc1 ON tc1.track_id = c.a
                LEFT JOIN track_contexts tc2 ON tc2.track_id = c.b
            ),
            blended AS (
                SELECT
                    a, b, support, content_score, collab_score,
                    (1 - support::double precision / (support + @pivot)) * content_score
                    + (support::double precision / (support + @pivot)) * collab_score AS score
                FROM scored
            ),
            both_directions AS (
                SELECT a AS track_id, b AS similar_track_id, score, content_score, collab_score, support
                FROM blended
                UNION ALL
                SELECT b, a, score, content_score, collab_score, support
                FROM blended
            ),
            ranked AS (
                SELECT *,
                       ROW_NUMBER() OVER (PARTITION BY track_id ORDER BY score DESC, similar_track_id) AS rank
                FROM both_directions
                WHERE score >= @min_score
            )
            INSERT INTO track_similarity (
                track_id, similar_track_id, score, content_score, collab_score, support, computed_at)
            SELECT track_id, similar_track_id, score, content_score, collab_score, support, now()
            FROM ranked
            WHERE rank <= @top_k;
            """;

        var parameters = new[]
        {
            Parameter("genre_core", NpgsqlDbType.Integer, GenreCoreSize),
            Parameter("window", NpgsqlDbType.Integer, CoOccurrenceWindowSeconds),
            Parameter("max_playlist", NpgsqlDbType.Integer, MaxCuratedPlaylistSize),
            Parameter("w_artist", NpgsqlDbType.Double, 0.45),
            Parameter("w_album", NpgsqlDbType.Double, 0.20),
            Parameter("w_genre", NpgsqlDbType.Double, 0.20),
            Parameter("w_year", NpgsqlDbType.Double, 0.10),
            Parameter("w_duration", NpgsqlDbType.Double, 0.05),
            Parameter("shrinkage", NpgsqlDbType.Double, Options.CollaborativeShrinkage),
            Parameter("pivot", NpgsqlDbType.Double, Options.CollaborativeBlendPivot),
            Parameter("min_score", NpgsqlDbType.Double, MinimumStoredScore),
            Parameter("top_k", NpgsqlDbType.Integer, Options.SimilarTopK),
        };

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await db.Database.ExecuteSqlRawAsync("DELETE FROM track_similarity", ct);
        var written = await db.Database.ExecuteSqlRawAsync(sql, parameters, ct);

        await transaction.CommitAsync(ct);

        logger.LogInformation("Rebuilt track similarity: {Count} neighbour rows", written);
    }

    public async Task PruneAsync(CancellationToken ct = default)
    {
        var eventCutoff = DateTimeOffset.UtcNow.AddDays(-Options.EventRetentionDays);
        var impressionCutoff = DateTimeOffset.UtcNow.AddDays(-Options.ImpressionRetentionDays);

        var events = await db.PlaybackEvents.Where(e => e.OccurredAt < eventCutoff).ExecuteDeleteAsync(ct);
        var impressions = await db.RecommendationImpressions
            .Where(i => i.ShownAt < impressionCutoff)
            .ExecuteDeleteAsync(ct);

        var runs = await db.RecommendationRuns
            .Where(r => r.StartedAt < impressionCutoff)
            .ExecuteDeleteAsync(ct);

        if (events + impressions + runs > 0)
        {
            logger.LogInformation(
                "Pruned {Events} events, {Impressions} impressions and {Runs} run records",
                events, impressions, runs);
        }

        await PruneOrphanTagsAsync(ct);
    }

    public async Task PruneOrphanTagsAsync(CancellationToken ct = default)
    {
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
            storage.DeleteCover(path);

        foreach (var path in imagePaths)
            storage.Delete(path);

        if (albums + artists + genres > 0)
        {
            logger.LogInformation(
                "Pruned {Albums} orphaned albums, {Artists} artists and {Genres} genres",
                albums, artists, genres);
        }
    }

    private static NpgsqlParameter Parameter(string name, NpgsqlDbType type, object value) =>
        new(name, type) { Value = value };
}
