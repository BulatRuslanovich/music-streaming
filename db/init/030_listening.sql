-- Настройки слушателя, тексты и почасовая статистика прослушиваний (ListeningConfiguration).

CREATE TABLE user_settings (
    user_id uuid NOT NULL,
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
