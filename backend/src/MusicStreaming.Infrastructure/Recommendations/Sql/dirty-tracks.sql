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
