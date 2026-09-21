-- Настройки слушателя, тексты и почасовая статистика прослушиваний (ListeningConfiguration).

CREATE TABLE user_settings (
    user_id uuid NOT NULL,
    autoplay boolean NOT NULL,
    quality integer NOT NULL,
    data_saver boolean NOT NULL,
    time_zone character varying(64) NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_user_settings PRIMARY KEY (user_id),
    CONSTRAINT fk_user_settings_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

CREATE TABLE track_lyrics (
    track_id uuid NOT NULL,
    plain text NOT NULL,
    synced jsonb NOT NULL,
    source integer NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_track_lyrics PRIMARY KEY (track_id),
    CONSTRAINT fk_track_lyrics_tracks_track_id FOREIGN KEY (track_id) REFERENCES tracks (id) ON DELETE CASCADE
);

CREATE TABLE listening_stats (
    user_id uuid NOT NULL,
    hour timestamp with time zone NOT NULL,
    track_id uuid NOT NULL,
    play_count integer NOT NULL,
    listened_seconds bigint NOT NULL,
    CONSTRAINT pk_listening_stats PRIMARY KEY (user_id, hour, track_id),
    CONSTRAINT fk_listening_stats_tracks_track_id FOREIGN KEY (track_id) REFERENCES tracks (id) ON DELETE CASCADE,
    CONSTRAINT fk_listening_stats_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

CREATE INDEX ix_listening_stats_track_id ON listening_stats (track_id);
