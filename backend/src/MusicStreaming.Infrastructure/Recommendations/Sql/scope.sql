SELECT DISTINCT track_id AS "Value"
FROM (
    SELECT unnest(@dirty) AS track_id
    UNION SELECT a FROM similarity_pairs WHERE b = ANY(@dirty)
    UNION SELECT b FROM similarity_pairs WHERE a = ANY(@dirty)
    UNION SELECT track_id FROM track_similarity WHERE similar_track_id = ANY(@dirty)
    UNION SELECT similar_track_id FROM track_similarity WHERE track_id = ANY(@dirty)
) reach;
