-- Сырые события воспроизведения: из них на лету считаются вкус слушателя, топ недели, итоги месяца
-- и статистика рекомендаций. Хранятся полгода (чистит обслуживание раз в шесть часов).

CREATE TABLE playback_events (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    track_id uuid,
    entity_id uuid,
    type integer NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    position_seconds integer NOT NULL,
    listened_seconds integer NOT NULL,
    duration_seconds integer NOT NULL,
    session_id uuid NOT NULL,
    source character varying(64),
    CONSTRAINT pk_playback_events PRIMARY KEY (id),
    CONSTRAINT fk_playback_events_tracks_track_id FOREIGN KEY (track_id) REFERENCES tracks (id) ON DELETE CASCADE,
    CONSTRAINT fk_playback_events_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

CREATE INDEX ix_playback_events_occurred_at ON playback_events (occurred_at);

CREATE INDEX ix_playback_events_track_id ON playback_events (track_id);

CREATE INDEX ix_playback_events_user_id_occurred_at ON playback_events (user_id, occurred_at);

CREATE INDEX ix_playback_events_user_id_track_id_occurred_at ON playback_events (user_id, track_id, occurred_at);
