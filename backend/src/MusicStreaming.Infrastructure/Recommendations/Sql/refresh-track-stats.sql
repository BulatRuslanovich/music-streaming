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
