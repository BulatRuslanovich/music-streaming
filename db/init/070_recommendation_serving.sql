-- То, что рекомендательный воркер кладёт, а API отдаёт (RecommendationServingConfiguration).

CREATE TABLE recommendation_cache (
    user_id uuid NOT NULL,
    shelf_key character varying(120) NOT NULL,
    position integer NOT NULL,
    payload jsonb NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_recommendation_cache PRIMARY KEY (user_id, shelf_key),
    CONSTRAINT fk_recommendation_cache_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

CREATE INDEX ix_recommendation_cache_user_id_position ON recommendation_cache (user_id, position);

CREATE TABLE daily_mixes (
    user_id uuid NOT NULL,
    local_date date NOT NULL,
    track_ids jsonb NOT NULL,
    CONSTRAINT pk_daily_mixes PRIMARY KEY (user_id, local_date),
    CONSTRAINT fk_daily_mixes_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

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
