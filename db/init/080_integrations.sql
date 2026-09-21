-- Внешние сервисы: привязка Last.fm и очередь исходящих задач (IntegrationConfiguration).

CREATE TABLE lastfm_accounts (
    user_id uuid NOT NULL,
    username character varying(100) NOT NULL,
    session_key character varying(2000) NOT NULL,
    enabled boolean NOT NULL,
    connected_at timestamp with time zone NOT NULL,
    last_scrobble_at timestamp with time zone,
    CONSTRAINT pk_lastfm_accounts PRIMARY KEY (user_id),
    CONSTRAINT fk_lastfm_accounts_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

CREATE TABLE outbound_jobs (
    id uuid NOT NULL,
    kind integer NOT NULL,
    user_id uuid NOT NULL,
    payload jsonb NOT NULL,
    dedupe_key character varying(200) NOT NULL,
    attempts integer NOT NULL,
    next_attempt_at timestamp with time zone NOT NULL,
    state integer NOT NULL,
    last_error character varying(500),
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_outbound_jobs PRIMARY KEY (id),
    CONSTRAINT fk_outbound_jobs_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

CREATE INDEX ix_outbound_jobs_state_next_attempt_at ON outbound_jobs (state, next_attempt_at);

CREATE INDEX ix_outbound_jobs_user_id ON outbound_jobs (user_id);

CREATE UNIQUE INDEX ix_outbound_jobs_dedupe_key ON outbound_jobs (dedupe_key);
