-- Расширения и функции, на которые опираются остальные файлы.
--
-- search_rank повторяет SearchRank.Evaluate из Application/Common: сортировка выдачи поиска
-- считается в базе, чтобы не тащить всю выборку в память ради одного ORDER BY.

CREATE EXTENSION IF NOT EXISTS pg_trgm;

CREATE OR REPLACE FUNCTION search_rank(value text, term text) RETURNS integer AS $$
    SELECT CASE
        WHEN value = term                                    THEN 0
        WHEN starts_with(value, term)                        THEN 1
        WHEN position(' ' || term in ' ' || value) > 0       THEN 2
        WHEN position(term in value) > 0                     THEN 3
        ELSE 4
    END;
$$ LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE;

