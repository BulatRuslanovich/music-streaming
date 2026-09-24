-- То, что рекомендательный воркер кладёт, а API отдаёт (RecommendationServingConfiguration).

CREATE TABLE recommendation_cache (
    user_id uuid NOT NULL,
    shelf_key character varying(120) NOT NULL,
    position integer NOT NULL,
    payload jsonb NOT NULL,
    generated_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    run_id uuid NOT NULL,
    CONSTRAINT pk_recommendation_cache PRIMARY KEY (user_id, shelf_key),
    CONSTRAINT fk_recommendation_cache_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

CREATE INDEX ix_recommendation_cache_user_id_position ON recommendation_cache (user_id, position);

CREATE TABLE daily_mixes (
    user_id uuid NOT NULL,
    local_date date NOT NULL,
    track_ids jsonb NOT NULL,
    generated_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_daily_mixes PRIMARY KEY (user_id, local_date),
    CONSTRAINT fk_daily_mixes_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

CREATE TABLE recommendation_impressions (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    track_id uuid NOT NULL,
    shelf_key character varying(120) NOT NULL,
    position integer NOT NULL,
    shown_at timestamp with time zone NOT NULL,
    clicked_at timestamp with time zone,
    CONSTRAINT pk_recommendation_impressions PRIMARY KEY (id),
    CONSTRAINT fk_recommendation_impressions_tracks_track_id FOREIGN KEY (track_id) REFERENCES tracks (id) ON DELETE CASCADE,
    CONSTRAINT fk_recommendation_impressions_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

CREATE INDEX ix_recommendation_impressions_shown_at ON recommendation_impressions (shown_at);

CREATE INDEX ix_recommendation_impressions_track_id ON recommendation_impressions (track_id);

CREATE INDEX ix_recommendation_impressions_user_id_shelf_key_shown_at ON recommendation_impressions (user_id, shelf_key, shown_at);

CREATE INDEX ix_recommendation_impressions_user_id_track_id_shown_at ON recommendation_impressions (user_id, track_id, shown_at);

CREATE TABLE recommendation_runs (
    id uuid NOT NULL,
    user_id uuid,
    trigger integer NOT NULL,
    started_at timestamp with time zone NOT NULL,
    duration_ms integer NOT NULL,
    candidate_count integer NOT NULL,
    shelf_count integer NOT NULL,
    status integer NOT NULL,
    error character varying(2000),
    CONSTRAINT pk_recommendation_runs PRIMARY KEY (id)
);

CREATE INDEX ix_recommendation_runs_started_at ON recommendation_runs (started_at);

CREATE TABLE recommendation_suppressions (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    target integer NOT NULL,
    target_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone,
    CONSTRAINT pk_recommendation_suppressions PRIMARY KEY (id),
    CONSTRAINT fk_recommendation_suppressions_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

CREATE INDEX ix_recommendation_suppressions_expires_at ON recommendation_suppressions (expires_at);

CREATE UNIQUE INDEX ix_recommendation_suppressions_user_id_target_target_id ON recommendation_suppressions (user_id, target, target_id);
