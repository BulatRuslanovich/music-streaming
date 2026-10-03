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
