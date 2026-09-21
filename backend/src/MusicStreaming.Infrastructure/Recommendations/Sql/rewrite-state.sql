-- Продолжение fingerprints.sql: записывает отпечатки области обратно.
INSERT INTO track_similarity_state (track_id, fingerprint, computed_at)
SELECT f.track_id, f.fingerprint, now()
FROM fingerprints f
WHERE @whole_library OR f.track_id = ANY(@scope)
ON CONFLICT (track_id) DO UPDATE
SET fingerprint = EXCLUDED.fingerprint, computed_at = EXCLUDED.computed_at;
