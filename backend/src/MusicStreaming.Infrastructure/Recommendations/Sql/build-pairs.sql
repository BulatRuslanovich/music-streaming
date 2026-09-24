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

-- Составной, а не два раздельных: и tag_core здесь, и tag_dot в score.sql соединяются
-- по паре (track_id, name), и раздельные индексы эту пару не покрывают.
CREATE INDEX ON similarity_tag_vectors (track_id, name);
CREATE INDEX ON similarity_tag_vectors (name);

-- ANALYZE идёт сразу за наполнением, а не в конце файла: tag_core ниже читает эту таблицу,
-- и без статистики по колонкам планировщик берёт для неё оценки по умолчанию.
ANALYZE similarity_tag_vectors;

CREATE TEMP TABLE similarity_tag_norms ON COMMIT DROP AS
SELECT track_id, sqrt(SUM(weight * weight)) AS norm
FROM similarity_tag_vectors
GROUP BY track_id;

CREATE INDEX ON similarity_tag_norms (track_id);
ANALYZE similarity_tag_norms;

CREATE TEMP TABLE similarity_sessions ON COMMIT DROP AS
SELECT DISTINCT ON (session_id, track_id)
           session_id, track_id, occurred_at
    FROM playback_events
    WHERE track_id IS NOT NULL
      AND session_id <> '00000000-0000-0000-0000-000000000000'::uuid
      -- TrackStarted, TrackCompleted, TrackSkipped: a track was actually put on.
      AND type IN (1, 3, 4)
    ORDER BY session_id, track_id, occurred_at;

-- Соседство внутри сессии — самое дорогое самосоединение в файле, и число сессий на трек
-- планировщику надо знать до того, как он выберет способ соединения.
CREATE INDEX ON similarity_sessions (session_id);
ANALYZE similarity_sessions;

CREATE TEMP TABLE similarity_playlists ON COMMIT DROP AS
SELECT playlist_id
    FROM playlist_tracks
    GROUP BY playlist_id
    HAVING COUNT(*) <= @max_playlist;

CREATE INDEX ON similarity_playlists (playlist_id);
ANALYZE similarity_playlists;

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
ANALYZE similarity_contexts;

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
-- Шапка здесь не ради качества, а ради того, чтобы соединение оставалось ограниченным.
-- Альбом — это уникальная пара (artist_id, normalized_title), поэтому плохо размеченный импорт,
-- где тысячи файлов несут один и тот же Album, схлопывается в одну строку альбома, и
-- самосоединение по album_id даёт N²/2 пар с одного альбома. Нормальный альбом заведомо меньше
-- @album_core, так что на здоровых данных выборка не меняется вовсе.
album_core AS (
    SELECT id, album_id
    FROM (
        SELECT t.id, t.album_id,
               ROW_NUMBER() OVER (
                   PARTITION BY t.album_id
                   ORDER BY COALESCE(s.popularity_score, 0) DESC, t.created_at DESC) AS rank
        FROM tracks t
        LEFT JOIN track_stats s ON s.track_id = t.id
        WHERE t.album_id IS NOT NULL
    ) ranked
    WHERE rank <= @album_core
),
album_pairs AS (
    SELECT a1.id AS a, a2.id AS b
    FROM album_core a1
    JOIN album_core a2 ON a2.album_id = a1.album_id AND a2.id > a1.id
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
-- Корзины по звучанию — один из семи генераторов пар, и на библиотеке с редкой разметкой и
-- небольшой историей он может оказаться единственным сработавшим. Поэтому он остался, но
-- корзины теперь кластерные: число пар примерно то же, «культурная» таблица выигрывает от
-- звукового пространства, и при этом ни один косинус в score.sql не попадает.
cluster_core AS (
    SELECT track_id, cluster_id
    FROM (
        SELECT
            e.track_id,
            e.cluster_id,
            ROW_NUMBER() OVER (
                PARTITION BY e.cluster_id
                ORDER BY COALESCE(s.popularity_score, 0) DESC, e.track_id) AS rank
        FROM track_embeddings e
        LEFT JOIN track_stats s ON s.track_id = e.track_id
        WHERE e.succeeded AND e.cluster_id IS NOT NULL
    ) ranked
    WHERE rank <= @audio_core
),
audio_pairs AS (
    SELECT c1.track_id AS a, c2.track_id AS b
    FROM cluster_core c1
    JOIN cluster_core c2
      ON c2.cluster_id = c1.cluster_id
     AND c2.track_id > c1.track_id
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

ANALYZE similarity_pairs;
