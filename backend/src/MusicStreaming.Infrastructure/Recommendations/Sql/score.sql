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
    LEFT JOIN similarity_tag_norms tn1 ON tn1.track_id = c.a
    LEFT JOIN similarity_tag_norms tn2 ON tn2.track_id = c.b
    LEFT JOIN tag_dot td ON td.a = c.a AND td.b = c.b
),
-- Тега, которого нет ни у одной из сторон, не тянет пару к середине: его вес возвращается
-- метаданным. Иначе трек без разметки проигрывал бы одним фактом её отсутствия.
--
-- Звучание сюда больше не входит. Эта таблица отвечает на вопрос «что с этим треком роднит
-- культура»: те же люди, те же плейлисты, те же ярлыки, тот же альбом. На вопрос «что звучит
-- похоже» отвечает косинус эмбеддингов, и он считается в памяти по матрице, а не собирается
-- здесь из темпа с яркостью.
assembled AS (
    SELECT
        a, b, support, collab_score,
        CASE WHEN tag_score IS NULL THEN base_metadata / (1 - @w_tag)
             ELSE base_metadata + @w_tag * tag_score END AS metadata_score
    FROM raw_scored
),
blended AS (
    SELECT
        a, b, support, metadata_score AS content_score, collab_score,
        (1 - support::double precision / (support + @pivot)) * metadata_score
        + (support::double precision / (support + @pivot)) * collab_score AS score
    FROM assembled
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
      AND (@whole_library OR track_id = ANY(@scope))
)
INSERT INTO track_similarity (
    track_id, similar_track_id, score, content_score, collab_score, support, computed_at)
SELECT track_id, similar_track_id, score, content_score, collab_score, support, now()
FROM ranked
WHERE rank <= @top_k;
