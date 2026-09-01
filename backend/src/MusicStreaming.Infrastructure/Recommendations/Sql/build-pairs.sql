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
