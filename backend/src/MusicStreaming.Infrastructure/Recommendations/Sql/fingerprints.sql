-- Общее начало для dirty-tracks.sql и rewrite-state.sql: один и тот же отпечаток решает,
-- что считать изменившимся, и им же он записывается обратно. Пока эти два запроса держали
-- по своей копии, любое расхождение означало бы, что инкрементальный проход перестаёт
-- сходиться к полному пересчёту, и заметить это было бы нечем.
--
-- Файл не самостоятелен: SimilaritySql приклеивает его к тому, что запрос делает дальше.
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
            -- Кластер эмбеддинга, а не DSP-признаки: именно он решает, с кем трек вообще
            -- образует пару (см. cluster_core в build-pairs.sql). Переанализ темпа и
            -- яркости пары не меняет и пересчёт гонять не должен.
            e.cluster_id,
            td.digest,
            pd.plays, pd.last_at,
            ld.digest)) AS fingerprint
    FROM tracks t
    LEFT JOIN credit_digest cd ON cd.track_id = t.id
    LEFT JOIN track_embeddings e ON e.track_id = t.id
    LEFT JOIN tag_digest td ON td.track_id = t.id
    LEFT JOIN play_digest pd ON pd.track_id = t.id
    LEFT JOIN playlist_digest ld ON ld.track_id = t.id
)
