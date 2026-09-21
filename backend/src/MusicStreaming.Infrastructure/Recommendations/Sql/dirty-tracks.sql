-- Продолжение fingerprints.sql: треки, чей отпечаток разошёлся с записанным.
SELECT f.track_id AS "Value"
FROM fingerprints f
LEFT JOIN track_similarity_state s ON s.track_id = f.track_id
WHERE s.fingerprint IS DISTINCT FROM f.fingerprint;
