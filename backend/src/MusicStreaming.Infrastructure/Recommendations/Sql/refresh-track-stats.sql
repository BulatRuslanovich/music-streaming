WITH recent AS (
    SELECT track_id, COUNT(*) AS plays
    FROM playback_events
    WHERE track_id IS NOT NULL
      -- TrackCompleted, TrackSkipped: the events that end a play attempt.
      AND type IN (3, 4)
      AND occurred_at >= now() - make_interval(days => 30)
    GROUP BY track_id
),
shown AS (
    -- Сколько раз трек показали в рекомендациях: чем чаще предлагали впустую, тем слабее
    -- надбавка новизны в очереди радио.
    --
    -- Окно то же, что у recent, и оно не сужает смысл: надбавка живёт, пока треку меньше
    -- QueueBuilder.NewTrackDays (две недели), так что показ трёхмесячной давности на неё уже
    -- не влияет. Зато запрос перестаёт читать всю таблицу и идёт по индексу shown_at.
    SELECT track_id, COUNT(*) AS impressions
    FROM recommendation_impressions
    WHERE shown_at >= now() - make_interval(days => 30)
    GROUP BY track_id
),
abandoned AS (
    -- Брошенные в первую пятую часть. Три таких — и надбавка новизны снимается совсем:
    -- трек уже показали достаточно, чтобы понять, что его не дослушивают.
    SELECT track_id, COUNT(*) AS drops
    FROM playback_events
    WHERE track_id IS NOT NULL
      AND type = 4
      AND duration_seconds > 0
      AND occurred_at >= now() - make_interval(days => 30)
      -- Каст обязателен: обе колонки integer, и целочисленное деление давало бы 0 для любого
      -- неполного прослушивания. Условие тогда читается как «прослушано меньше, чем длится»,
      -- то есть считает вообще каждый скип, а не брошенный в начале.
      AND listened_seconds::double precision / duration_seconds < 0.2
    GROUP BY track_id
),
rollup AS (
    SELECT
        a.track_id,
        SUM(a.play_count)                                        AS play_count,
        SUM(a.skip_count)                                        AS skip_count,
        MAX(a.last_played_at)                                    AS last_played_at
    FROM user_track_affinity a
    GROUP BY a.track_id
)
INSERT INTO track_stats (
    track_id, play_count, skip_count, skip_rate, popularity_score,
    shown_count, skipped_early_count, last_played_at, computed_at)
SELECT
    t.id,
    COALESCE(r.play_count, 0),
    COALESCE(r.skip_count, 0),
    CASE WHEN COALESCE(r.play_count, 0) > 0
         THEN r.skip_count::double precision / r.play_count ELSE 0 END,
    -- Volume squashed into [0, 1); recent plays count double so that popularity
    -- tracks what the library listens to now, not what it listened to a year ago.
    (COALESCE(recent.plays, 0) * 2 + COALESCE(r.play_count, 0))::double precision
        / ((COALESCE(recent.plays, 0) * 2 + COALESCE(r.play_count, 0)) + 10),
    COALESCE(shown.impressions, 0),
    COALESCE(abandoned.drops, 0),
    r.last_played_at,
    now()
FROM tracks t
LEFT JOIN rollup r ON r.track_id = t.id
LEFT JOIN recent ON recent.track_id = t.id
LEFT JOIN shown ON shown.track_id = t.id
LEFT JOIN abandoned ON abandoned.track_id = t.id
ON CONFLICT (track_id) DO UPDATE SET
    play_count = EXCLUDED.play_count,
    skip_count = EXCLUDED.skip_count,
    skip_rate = EXCLUDED.skip_rate,
    popularity_score = EXCLUDED.popularity_score,
    shown_count = EXCLUDED.shown_count,
    skipped_early_count = EXCLUDED.skipped_early_count,
    last_played_at = EXCLUDED.last_played_at,
    computed_at = EXCLUDED.computed_at
-- Без этой отсечки проход переписывал каждую строку таблицы каждые шесть часов и оставлял по
-- мёртвому кортежу на трек — на пятидесяти тысячах треков это двести тысяч в сутки под индексом
-- по популярности. computed_at в сравнение не входит: он меняется всегда, и с ним отсечка
-- не срабатывала бы никогда. Читать его при этом некому — он пишется и всё.
WHERE track_stats.play_count IS DISTINCT FROM EXCLUDED.play_count
   OR track_stats.skip_count IS DISTINCT FROM EXCLUDED.skip_count
   OR track_stats.skip_rate IS DISTINCT FROM EXCLUDED.skip_rate
   OR track_stats.popularity_score IS DISTINCT FROM EXCLUDED.popularity_score
   OR track_stats.shown_count IS DISTINCT FROM EXCLUDED.shown_count
   OR track_stats.skipped_early_count IS DISTINCT FROM EXCLUDED.skipped_early_count
   OR track_stats.last_played_at IS DISTINCT FROM EXCLUDED.last_played_at;
