// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Infrastructure.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace MusicStreaming.Infrastructure.Recommendations;

/// <summary>Что сделал проход пересчёта: пригодилось тестам и логам, чтобы режим был виден.</summary>
public record SimilarityRefresh(bool Ran, bool WholeLibrary, int Tracks, int Rows)
{
    public static readonly SimilarityRefresh Unchanged = new(false, false, 0, 0);
}

public class SimilarityMaintenance(
    ApplicationDbContext db,
    IMusicStorage storage,
    IOptions<RecommendationOptions> options,
    ILogger<SimilarityMaintenance> logger)
{
    private const int CoOccurrenceWindowSeconds = 1800;
    private const int MaxCuratedPlaylistSize = 100;
    private const int ArtistCoreSize = 200;
    private const int GenreCoreSize = 60;
    private const int AudioBucketCoreSize = 120;
    private const int TagCoreSize = 80;
    private const int MinimumSharedTags = 2;
    private const double MinimumPairingTagWeight = 0.3;

    /// <summary>Тег исполнителя описывает трек слабее, чем тег самого трека.</summary>
    private const double ArtistTagShare = 0.6;
    private const double TagWeight = 0.25;

    /// <summary>Ниже этой уверенности оценка тональности — шум, и совпадение ничего не значит.</summary>
    private const double MinimumKeyConfidence = 0.4;

    // Веса аудио-схожести. Темп, энергия, яркость, спад, динамика и громкость есть всегда;
    // тембр и тональность появляются только после анализа второй версии, поэтому их доли
    // вынесены отдельно, чтобы их можно было честно вернуть остальным.
    private const double TempoWeight = 0.28;
    private const double TimbreWeight = 0.24;
    private const double KeyWeight = 0.03;
    private const double AudioBaseWeight = TempoWeight + 0.16 + 0.10 + 0.06 + 0.08 + 0.05;
    private const double MinimumStoredScore = 0.05;

    /// <summary>За этой долей изменившихся треков область охватывает почти всё, и полная дешевле.</summary>
    private const double FullRebuildShare = 0.25;

    /// <summary>Как часто библиотека пересобирается целиком, даже если ничего не менялось.</summary>
    private const int FullRebuildIntervalHours = 24;
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

    private const string AnalyzeInputs =
        "ANALYZE tracks, track_artists, track_stats, track_audio_features, "
        + "track_tags, artist_tags, playback_events, playlist_tracks;";

    private const string DirtyTracksSql =
        """
        WITH tag_digest AS (
            SELECT track_id,
                   md5(string_agg(name || ':' || round(weight::numeric, 3)::text, ',' ORDER BY name)) AS digest
            FROM (
                SELECT tt.track_id, tt.name, tt.weight FROM track_tags tt
                UNION ALL
                SELECT ta.track_id, at.name, at.weight
                FROM track_artists ta JOIN artist_tags at ON at.artist_id = ta.artist_id
            ) parts
            GROUP BY track_id
        ),
        play_digest AS (
            SELECT track_id, COUNT(*) AS plays, MAX(occurred_at) AS last_at
            FROM playback_events
            WHERE track_id IS NOT NULL AND type IN (1, 3, 4)
            GROUP BY track_id
        ),
        playlist_digest AS (
            SELECT track_id, md5(string_agg(playlist_id::text, ',' ORDER BY playlist_id)) AS digest
            FROM playlist_tracks
            GROUP BY track_id
        ),
        credit_digest AS (
            SELECT track_id, md5(string_agg(artist_id::text, ',' ORDER BY artist_id)) AS digest
            FROM track_artists
            GROUP BY track_id
        ),
        fingerprints AS (
            SELECT
                t.id AS track_id,
                md5(concat_ws('|',
                    t.artist_id, t.album_id, t.genre_id, t.year, t.duration_seconds,
                    cd.digest,
                    af.analyzed_at, af.algorithm_version, af.succeeded,
                    td.digest,
                    pd.plays, pd.last_at,
                    ld.digest)) AS fingerprint
            FROM tracks t
            LEFT JOIN credit_digest cd ON cd.track_id = t.id
            LEFT JOIN track_audio_features af ON af.track_id = t.id
            LEFT JOIN tag_digest td ON td.track_id = t.id
            LEFT JOIN play_digest pd ON pd.track_id = t.id
            LEFT JOIN playlist_digest ld ON ld.track_id = t.id
        )
        SELECT f.track_id AS "Value"
        FROM fingerprints f
        LEFT JOIN track_similarity_state s ON s.track_id = f.track_id
        WHERE s.fingerprint IS DISTINCT FROM f.fingerprint;
        """;

    private const string RewriteStateSql =
        """
        WITH tag_digest AS (
            SELECT track_id,
                   md5(string_agg(name || ':' || round(weight::numeric, 3)::text, ',' ORDER BY name)) AS digest
            FROM (
                SELECT tt.track_id, tt.name, tt.weight FROM track_tags tt
                UNION ALL
                SELECT ta.track_id, at.name, at.weight
                FROM track_artists ta JOIN artist_tags at ON at.artist_id = ta.artist_id
            ) parts
            GROUP BY track_id
        ),
        play_digest AS (
            SELECT track_id, COUNT(*) AS plays, MAX(occurred_at) AS last_at
            FROM playback_events
            WHERE track_id IS NOT NULL AND type IN (1, 3, 4)
            GROUP BY track_id
        ),
        playlist_digest AS (
            SELECT track_id, md5(string_agg(playlist_id::text, ',' ORDER BY playlist_id)) AS digest
            FROM playlist_tracks
            GROUP BY track_id
        ),
        credit_digest AS (
            SELECT track_id, md5(string_agg(artist_id::text, ',' ORDER BY artist_id)) AS digest
            FROM track_artists
            GROUP BY track_id
        )
        INSERT INTO track_similarity_state (track_id, fingerprint, computed_at)
        SELECT
            t.id,
            md5(concat_ws('|',
                t.artist_id, t.album_id, t.genre_id, t.year, t.duration_seconds,
                cd.digest,
                af.analyzed_at, af.algorithm_version, af.succeeded,
                td.digest,
                pd.plays, pd.last_at,
                ld.digest)),
            now()
        FROM tracks t
        LEFT JOIN credit_digest cd ON cd.track_id = t.id
        LEFT JOIN track_audio_features af ON af.track_id = t.id
        LEFT JOIN tag_digest td ON td.track_id = t.id
        LEFT JOIN play_digest pd ON pd.track_id = t.id
        LEFT JOIN playlist_digest ld ON ld.track_id = t.id
        WHERE @whole_library OR t.id = ANY(@scope)
        ON CONFLICT (track_id) DO UPDATE
        SET fingerprint = EXCLUDED.fingerprint, computed_at = EXCLUDED.computed_at;
        """;

    private const string ScopeSql =
        """
        SELECT DISTINCT track_id AS "Value"
        FROM (
            SELECT unnest(@dirty) AS track_id
            UNION SELECT a FROM similarity_pairs WHERE b = ANY(@dirty)
            UNION SELECT b FROM similarity_pairs WHERE a = ANY(@dirty)
            UNION SELECT track_id FROM track_similarity WHERE similar_track_id = ANY(@dirty)
            UNION SELECT similar_track_id FROM track_similarity WHERE track_id = ANY(@dirty)
        ) reach;
        """;

    private const string BuildPairsSql =
        """
        CREATE TEMP TABLE similarity_tag_vectors ON COMMIT DROP AS
        SELECT track_id, name, MAX(weight) AS weight
            FROM (
                SELECT tt.track_id, tt.name, tt.weight
                FROM track_tags tt
                UNION ALL
                SELECT ta.track_id, at.name, at.weight * @artist_tag_share
                FROM track_artists ta
                JOIN artist_tags at ON at.artist_id = ta.artist_id
            ) parts
            GROUP BY track_id, name;

        CREATE INDEX ON similarity_tag_vectors (track_id);
        CREATE INDEX ON similarity_tag_vectors (name);

        CREATE TEMP TABLE similarity_tag_norms ON COMMIT DROP AS
        SELECT track_id, sqrt(SUM(weight * weight)) AS norm
        FROM similarity_tag_vectors
        GROUP BY track_id;

        CREATE INDEX ON similarity_tag_norms (track_id);

        CREATE TEMP TABLE similarity_sessions ON COMMIT DROP AS
        SELECT DISTINCT ON (session_id, track_id)
                   session_id, track_id, occurred_at
            FROM playback_events
            WHERE track_id IS NOT NULL
              AND session_id <> '00000000-0000-0000-0000-000000000000'::uuid
              -- TrackStarted, TrackCompleted, TrackSkipped: a track was actually put on.
              AND type IN (1, 3, 4)
            ORDER BY session_id, track_id, occurred_at;

        CREATE TEMP TABLE similarity_playlists ON COMMIT DROP AS
        SELECT playlist_id
            FROM playlist_tracks
            GROUP BY playlist_id
            HAVING COUNT(*) <= @max_playlist;

        CREATE TEMP TABLE similarity_contexts ON COMMIT DROP AS
        SELECT track_id, SUM(contexts) AS contexts
        FROM (
            SELECT track_id, COUNT(DISTINCT session_id) AS contexts
            FROM similarity_sessions GROUP BY track_id
            UNION ALL
            SELECT pt.track_id, COUNT(DISTINCT pt.playlist_id)
            FROM playlist_tracks pt
            JOIN similarity_playlists c ON c.playlist_id = pt.playlist_id
            GROUP BY pt.track_id
        ) parts
        GROUP BY track_id;

        CREATE INDEX ON similarity_contexts (track_id);

        CREATE TEMP TABLE similarity_pairs ON COMMIT DROP AS
        WITH artist_core AS (
            SELECT track_id, artist_id
            FROM (
                SELECT ta.track_id, ta.artist_id,
                       ROW_NUMBER() OVER (
                           PARTITION BY ta.artist_id
                           ORDER BY COALESCE(s.popularity_score, 0) DESC, t.created_at DESC) AS rank
                FROM track_artists ta
                JOIN tracks t ON t.id = ta.track_id
                LEFT JOIN track_stats s ON s.track_id = ta.track_id
            ) ranked
            WHERE rank <= @artist_core
        ),
        shared_artists AS (
            SELECT ta1.track_id AS a, ta2.track_id AS b, COUNT(*) AS shared
            FROM artist_core ta1
            JOIN artist_core ta2
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
        audio_core AS (
            SELECT track_id, tempo_bucket, energy_bucket, brightness_bucket
            FROM (
                SELECT
                    f.track_id,
                    ROUND(f.tempo_bpm / 10.0)::int AS tempo_bucket,
                    FLOOR(f.energy * 5)::int AS energy_bucket,
                    FLOOR(f.brightness * 5)::int AS brightness_bucket,
                    ROW_NUMBER() OVER (
                        PARTITION BY ROUND(f.tempo_bpm / 10.0)::int,
                                     FLOOR(f.energy * 5)::int,
                                     FLOOR(f.brightness * 5)::int
                        ORDER BY COALESCE(s.popularity_score, 0) DESC, f.track_id) AS rank
                FROM track_audio_features f
                LEFT JOIN track_stats s ON s.track_id = f.track_id
                WHERE f.succeeded AND f.tempo_bpm > 0 AND f.tempo_confidence >= 0.15
            ) ranked
            WHERE rank <= @audio_core
        ),
        audio_pairs AS (
            SELECT f1.track_id AS a, f2.track_id AS b
            FROM audio_core f1
            JOIN audio_core f2
              ON f2.tempo_bucket = f1.tempo_bucket
             AND f2.energy_bucket = f1.energy_bucket
             AND f2.brightness_bucket = f1.brightness_bucket
             AND f2.track_id > f1.track_id
        ),
        tag_core AS (
            SELECT track_id, name
            FROM (
                SELECT
                    v.track_id,
                    v.name,
                    ROW_NUMBER() OVER (
                        PARTITION BY v.name
                        ORDER BY v.weight DESC, COALESCE(s.popularity_score, 0) DESC, v.track_id) AS rank
                FROM similarity_tag_vectors v
                LEFT JOIN track_stats s ON s.track_id = v.track_id
                WHERE v.weight >= @min_tag_weight
            ) ranked
            WHERE rank <= @tag_core
        ),
        -- Один общий тег ничего не значит: «rock» стоит на половине библиотеки.
        tag_pairs AS (
            SELECT v1.track_id AS a, v2.track_id AS b
            FROM tag_core v1
            JOIN tag_core v2 ON v2.name = v1.name AND v2.track_id > v1.track_id
            GROUP BY 1, 2
            HAVING COUNT(*) >= @min_shared_tags
        ),
        session_cooc AS (
            SELECT p1.track_id AS a, p2.track_id AS b, COUNT(DISTINCT p1.session_id) AS support
                FROM similarity_sessions p1
                JOIN similarity_sessions p2
                  ON p2.session_id = p1.session_id
                 AND p2.track_id > p1.track_id
                 AND abs(extract(epoch FROM (p2.occurred_at - p1.occurred_at))) <= @window
                GROUP BY 1, 2
        ),
        playlist_cooc AS (
            SELECT pt1.track_id AS a, pt2.track_id AS b, COUNT(DISTINCT pt1.playlist_id) AS support
                FROM playlist_tracks pt1
                JOIN playlist_tracks pt2
                  ON pt2.playlist_id = pt1.playlist_id
                 AND pt2.track_id > pt1.track_id
                JOIN similarity_playlists c ON c.playlist_id = pt1.playlist_id
                GROUP BY 1, 2
        ),
        candidates AS (
            SELECT a, b, SUM(support) AS support
            FROM (
                SELECT a, b, 0 AS support FROM shared_artists
                UNION ALL SELECT a, b, 0 FROM album_pairs
                UNION ALL SELECT a, b, 0 FROM genre_pairs
                UNION ALL SELECT a, b, 0 FROM audio_pairs
                UNION ALL SELECT a, b, 0 FROM tag_pairs
                UNION ALL SELECT a, b, support FROM session_cooc
                UNION ALL SELECT a, b, support FROM playlist_cooc
            ) all_pairs
            GROUP BY a, b
        ),
        shared AS (
            SELECT c.a, c.b, c.support, COALESCE(sa.shared, 0) AS shared
            FROM candidates c
            LEFT JOIN shared_artists sa ON sa.a = c.a AND sa.b = c.b
        )
        SELECT a, b, support, shared FROM shared;

        CREATE INDEX ON similarity_pairs (a);
        CREATE INDEX ON similarity_pairs (b);

        ANALYZE similarity_pairs, similarity_tag_vectors, similarity_tag_norms, similarity_contexts;
        """;

    private const string ScoreSql =
        """
        WITH artist_counts AS (
            SELECT track_id, COUNT(*) AS credits
            FROM track_artists
            GROUP BY track_id
        ),
        -- Пересчитывается только то, что попало в область: у трека вне её строки остаются прежними,
        -- и переписывать их нечем — все его пары в область не попали.
        scoped AS (
            SELECT p.a, p.b, p.support, p.shared
            FROM similarity_pairs p
            WHERE @whole_library OR p.a = ANY(@scope) OR p.b = ANY(@scope)
        ),
        tag_dot AS (
            SELECT c.a, c.b, SUM(v1.weight * v2.weight) AS dot
            FROM scoped c
            JOIN similarity_tag_vectors v1 ON v1.track_id = c.a
            JOIN similarity_tag_vectors v2 ON v2.track_id = c.b AND v2.name = v1.name
            GROUP BY c.a, c.b
        ),
        raw_scored AS (
            SELECT
                c.a,
                c.b,
                c.support::int AS support,
                (@w_artist * COALESCE(c.shared::double precision
                    / NULLIF(ac1.credits + ac2.credits - c.shared, 0), 0)
                 + @w_album * CASE WHEN t1.album_id IS NOT NULL AND t1.album_id = t2.album_id
                                   THEN 1 ELSE 0 END
                 + @w_genre * CASE WHEN t1.genre_id IS NOT NULL AND t1.genre_id = t2.genre_id
                                   THEN 1 ELSE 0 END
                 + @w_year * CASE WHEN t1.year IS NULL OR t2.year IS NULL THEN 0
                                  ELSE exp(-abs(t1.year - t2.year) / 8.0) END
                 + @w_duration * exp(
                     -abs(t1.duration_seconds - t2.duration_seconds) / 120.0)
                ) AS base_metadata,
                CASE WHEN tn1.norm > 0 AND tn2.norm > 0 AND td.dot IS NOT NULL
                     THEN LEAST(1.0, td.dot / (tn1.norm * tn2.norm))
                     ELSE NULL END AS tag_score,
                af1.succeeded AND af2.succeeded AS has_audio,
                CASE WHEN af1.succeeded AND af2.succeeded THEN
                    @w_tempo * CASE WHEN af1.tempo_bpm IS NULL OR af1.tempo_bpm <= 0
                                         OR af2.tempo_bpm IS NULL OR af2.tempo_bpm <= 0 THEN 0.5
                                ELSE 0.5 + GREATEST(0, LEAST(1,
                                         LEAST(af1.tempo_confidence, af2.tempo_confidence)))
                                         * (exp(-abs(ln(af1.tempo_bpm / af2.tempo_bpm)) / 0.18) - 0.5)
                                END
                    + 0.16 * exp(-abs(af1.energy - af2.energy) / 0.18)
                    + 0.10 * exp(-abs(af1.brightness - af2.brightness) / 0.18)
                    + 0.06 * exp(-abs(af1.spectral_rolloff - af2.spectral_rolloff) / 0.18)
                    + 0.08 * exp(-abs(af1.dynamic_range_db - af2.dynamic_range_db) / 10.0)
                    + 0.05 * exp(-abs(af1.loudness_db - af2.loudness_db) / 8.0)
                ELSE NULL END AS audio_base,
                -- Тембровые векторы единичной длины, поэтому скалярное произведение это
                -- косинус; в 0..1 его переводит (1 + cos) / 2.
                -- array_length пустого массива это NULL, а не 0, поэтому обе стороны надо
                -- свести к нулю: иначе сравнение даёт NULL, ветка не срабатывает, и unnest
                -- молча дополняет короткий вектор нулями.
                CASE WHEN COALESCE(array_length(af1.timbre, 1), 0) = 0
                          OR COALESCE(array_length(af1.timbre, 1), 0)
                             <> COALESCE(array_length(af2.timbre, 1), 0)
                     THEN NULL
                     ELSE GREATEST(0, LEAST(1, (1 + (
                         SELECT COALESCE(SUM(x * y), 0)
                         FROM unnest(af1.timbre, af2.timbre) AS pair(x, y))) / 2))
                     END AS timbre_score,
                -- Тональность учитывается, только когда обе оценки уверенные.
                CASE WHEN af1.key IS NULL OR af2.key IS NULL
                          OR LEAST(af1.key_strength, af2.key_strength) < @key_confidence
                     THEN NULL
                     WHEN af1.key = af2.key AND af1.is_minor = af2.is_minor THEN 1
                     ELSE 0
                     END AS key_score,
                CASE WHEN c.support > 0
                     THEN LEAST(1.0, c.support::double precision
                              / NULLIF(sqrt(GREATEST(tc1.contexts, 1)::double precision
                                          * GREATEST(tc2.contexts, 1)), 0))
                          * (c.support::double precision / (c.support + @shrinkage))
                     ELSE 0 END AS collab_score
            FROM scoped c
            JOIN tracks t1 ON t1.id = c.a
            JOIN tracks t2 ON t2.id = c.b
            LEFT JOIN artist_counts ac1 ON ac1.track_id = c.a
            LEFT JOIN artist_counts ac2 ON ac2.track_id = c.b
            LEFT JOIN similarity_contexts tc1 ON tc1.track_id = c.a
            LEFT JOIN similarity_contexts tc2 ON tc2.track_id = c.b
            LEFT JOIN track_audio_features af1 ON af1.track_id = c.a
            LEFT JOIN track_audio_features af2 ON af2.track_id = c.b
            LEFT JOIN similarity_tag_norms tn1 ON tn1.track_id = c.a
            LEFT JOIN similarity_tag_norms tn2 ON tn2.track_id = c.b
            LEFT JOIN tag_dot td ON td.a = c.a AND td.b = c.b
        ),
        -- Признак, которого нет ни у одной из сторон, не тянет пару к середине: его вес
        -- возвращается остальным. Иначе трек, ещё не переанализированный после смены версии
        -- алгоритма, проигрывал бы только фактом отсутствия тембра.
        assembled AS (
            SELECT
                a, b, support, has_audio, collab_score,
                CASE WHEN tag_score IS NULL THEN base_metadata / (1 - @w_tag)
                     ELSE base_metadata + @w_tag * tag_score END AS metadata_score,
                CASE WHEN NOT has_audio THEN NULL
                     ELSE (audio_base
                           + COALESCE(@w_timbre * timbre_score, 0)
                           + COALESCE(@w_key * key_score, 0))
                          / (@w_audio_base
                             + CASE WHEN timbre_score IS NULL THEN 0 ELSE @w_timbre END
                             + CASE WHEN key_score IS NULL THEN 0 ELSE @w_key END)
                     END AS audio_score
            FROM raw_scored
        ),
        scored AS (
            SELECT
                a, b, support,
                CASE WHEN has_audio THEN 0.55 * metadata_score + 0.45 * audio_score
                     ELSE metadata_score END AS content_score,
                audio_score,
                collab_score
            FROM assembled
        ),
        blended AS (
            SELECT
                a, b, support, content_score, audio_score, collab_score,
                (1 - support::double precision / (support + @pivot)) * content_score
                + (support::double precision / (support + @pivot)) * collab_score AS score
            FROM scored
        ),
        both_directions AS (
            SELECT a AS track_id, b AS similar_track_id, score, content_score, audio_score, collab_score, support
            FROM blended
            UNION ALL
            SELECT b, a, score, content_score, audio_score, collab_score, support
            FROM blended
        ),
        ranked AS (
            SELECT *,
                   ROW_NUMBER() OVER (PARTITION BY track_id ORDER BY score DESC, similar_track_id) AS rank
            FROM both_directions
            WHERE score >= @min_score
              AND (@whole_library OR track_id = ANY(@scope))
        )
        INSERT INTO track_similarity (
            track_id, similar_track_id, score, content_score, audio_score, collab_score, support, computed_at)
        SELECT track_id, similar_track_id, score, content_score, audio_score, collab_score, support, now()
        FROM ranked
        WHERE rank <= @top_k;
        """;

    /// <summary>
    /// Пересчёт схожести. Что именно изменилось, определяется отпечатком входов каждого трека:
    /// не изменилось ничего — проход не делает вообще ничего; изменилось немного — пересобирается
    /// только затронутая область; изменилось много или подошёл срок — вся библиотека.
    /// </summary>
    public async Task<SimilarityRefresh> RefreshSimilarityAsync(CancellationToken ct = default)
    {
        var dirty = await DirtyTracksAsync(ct);
        var total = await db.Tracks.CountAsync(ct);
        var fullRebuildDue = await FullRebuildDueAsync(ct);

        if (dirty.Count == 0 && !fullRebuildDue)
        {
            logger.LogDebug("Track similarity is up to date: nothing changed since the last pass");
            return SimilarityRefresh.Unchanged;
        }

        // Область в долях библиотеки растёт быстрее числа изменившихся треков — у каждого из них
        // десятки соседей, — поэтому за порогом полная пересборка просто дешевле.
        var whole = fullRebuildDue
                    || total == 0
                    || dirty.Count >= total * FullRebuildShare;

        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();

        // Пересборка читает таблицы, которые сильно меняются между проходами: после крупного
        // импорта планировщик работает по устаревшей статистике и выбирает вложенные циклы там,
        // где нужен хеш-джойн, — запрос из секунд превращается в минуты. ANALYZE стоит доли секунды,
        // но проходу, которому нечего делать, не нужен и он.
        await db.Database.ExecuteSqlRawAsync(AnalyzeInputs, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await db.Database.ExecuteSqlRawAsync(BuildPairsSql, PairParameters(), ct);

        var scope = whole ? [] : await ScopeAsync(dirty, ct);

        if (!whole && scope.Count == 0)
        {
            await transaction.RollbackAsync(ct);
            logger.LogDebug("Track similarity is up to date: the changed tracks pair with nothing");
            return SimilarityRefresh.Unchanged;
        }

        await DeleteStaleAsync(whole, scope, ct);

        var written = await db.Database.ExecuteSqlRawAsync(ScoreSql, ScoreParameters(whole, scope), ct);

        await db.Database.ExecuteSqlRawAsync(
            RewriteStateSql, [WholeLibrary(whole), Scope(whole ? dirty : scope)], ct);

        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "{Mode} track similarity in {Elapsed:0.0} s: {Count} neighbour rows over {Scope} of {Total} tracks",
            whole ? "Rebuilt" : "Refreshed",
            System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalSeconds,
            written,
            whole ? total : scope.Count,
            total);

        return new SimilarityRefresh(true, whole, whole ? total : scope.Count, written);
    }

    private async Task<List<Guid>> DirtyTracksAsync(CancellationToken ct) =>
        await db.Database.SqlQueryRaw<Guid>(DirtyTracksSql).ToListAsync(ct);

    private async Task<bool> FullRebuildDueAsync(CancellationToken ct)
    {
        // Самый старый отпечаток — это момент последней полной пересборки: только она переписывает
        // их все. Периодически полная нужна, потому что популярность в отпечаток не входит, а от неё
        // зависит, какие треки представляют жанр или тег в парах.
        var oldest = await db.TrackSimilarityStates
            .OrderBy(state => state.ComputedAt)
            .Select(state => (DateTimeOffset?)state.ComputedAt)
            .FirstOrDefaultAsync(ct);

        return oldest is not { } moment
               || DateTimeOffset.UtcNow - moment >= TimeSpan.FromHours(FullRebuildIntervalHours);
    }

    private async Task<List<Guid>> ScopeAsync(List<Guid> dirty, CancellationToken ct) =>
        await db.Database.SqlQueryRaw<Guid>(ScopeSql, Dirty(dirty)).ToListAsync(ct);

    private async Task DeleteStaleAsync(bool whole, List<Guid> scope, CancellationToken ct)
    {
        if (whole)
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM track_similarity", ct);
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM track_similarity WHERE track_id = ANY(@scope)", [Scope(scope)], ct);
    }

    private NpgsqlParameter[] PairParameters() =>
    [
        Parameter("artist_core", NpgsqlDbType.Integer, ArtistCoreSize),
        Parameter("genre_core", NpgsqlDbType.Integer, GenreCoreSize),
        Parameter("audio_core", NpgsqlDbType.Integer, AudioBucketCoreSize),
        Parameter("tag_core", NpgsqlDbType.Integer, TagCoreSize),
        Parameter("min_shared_tags", NpgsqlDbType.Integer, MinimumSharedTags),
        Parameter("min_tag_weight", NpgsqlDbType.Double, MinimumPairingTagWeight),
        Parameter("artist_tag_share", NpgsqlDbType.Double, ArtistTagShare),
        Parameter("window", NpgsqlDbType.Integer, CoOccurrenceWindowSeconds),
        Parameter("max_playlist", NpgsqlDbType.Integer, MaxCuratedPlaylistSize),
    ];

    private NpgsqlParameter[] ScoreParameters(bool whole, List<Guid> scope) =>
    [
        // Вместе с @w_tag суммируются в единицу: тег-вектор частично замещает жанровый ярлык.
        Parameter("w_artist", NpgsqlDbType.Double, 0.35),
        Parameter("w_album", NpgsqlDbType.Double, 0.16),
        Parameter("w_genre", NpgsqlDbType.Double, 0.12),
        Parameter("w_year", NpgsqlDbType.Double, 0.08),
        Parameter("w_duration", NpgsqlDbType.Double, 0.04),
        Parameter("w_tag", NpgsqlDbType.Double, TagWeight),
        Parameter("shrinkage", NpgsqlDbType.Double, Options.CollaborativeShrinkage),
        Parameter("pivot", NpgsqlDbType.Double, Options.CollaborativeBlendPivot),
        Parameter("w_tempo", NpgsqlDbType.Double, TempoWeight),
        Parameter("w_timbre", NpgsqlDbType.Double, TimbreWeight),
        Parameter("w_key", NpgsqlDbType.Double, KeyWeight),
        Parameter("w_audio_base", NpgsqlDbType.Double, AudioBaseWeight),
        Parameter("key_confidence", NpgsqlDbType.Double, MinimumKeyConfidence),
        Parameter("min_score", NpgsqlDbType.Double, MinimumStoredScore),
        Parameter("top_k", NpgsqlDbType.Integer, Options.SimilarTopK),
        WholeLibrary(whole),
        Scope(scope),
    ];

    private static NpgsqlParameter Dirty(List<Guid> dirty) =>
        new("dirty", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = dirty.ToArray() };

    private static NpgsqlParameter Scope(List<Guid> scope) =>
        new("scope", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = scope.ToArray() };

    private static NpgsqlParameter WholeLibrary(bool whole) =>
        new("whole_library", NpgsqlDbType.Boolean) { Value = whole };

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

    private async Task PruneOrphanTagsAsync(CancellationToken ct = default)
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
