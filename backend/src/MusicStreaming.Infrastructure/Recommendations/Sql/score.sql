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
