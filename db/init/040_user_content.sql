-- То, что заводит сам слушатель: плейлисты, избранное, история (UserContentConfiguration).

CREATE TABLE playlists (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    name character varying(200) NOT NULL,
    description character varying(1000),
    cover_path character varying(400),
    is_public boolean NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_playlists PRIMARY KEY (id),
    CONSTRAINT fk_playlists_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

CREATE INDEX ix_playlists_created_at ON playlists (created_at);

CREATE INDEX ix_playlists_is_public_updated_at ON playlists (is_public, updated_at);

CREATE INDEX ix_playlists_user_id_name ON playlists (user_id, name);

CREATE TABLE playlist_tracks (
    id uuid NOT NULL,
    playlist_id uuid NOT NULL,
    track_id uuid NOT NULL,
    position integer NOT NULL,
    added_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_playlist_tracks PRIMARY KEY (id),
    CONSTRAINT fk_playlist_tracks_playlists_playlist_id FOREIGN KEY (playlist_id) REFERENCES playlists (id) ON DELETE CASCADE,
    CONSTRAINT fk_playlist_tracks_tracks_track_id FOREIGN KEY (track_id) REFERENCES tracks (id) ON DELETE CASCADE
);

CREATE INDEX ix_playlist_tracks_playlist_id_position ON playlist_tracks (playlist_id, position);

CREATE INDEX ix_playlist_tracks_track_id ON playlist_tracks (track_id);

CREATE UNIQUE INDEX ix_playlist_tracks_playlist_id_track_id ON playlist_tracks (playlist_id, track_id);

CREATE TABLE favorites (
    user_id uuid NOT NULL,
    track_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_favorites PRIMARY KEY (user_id, track_id),
    CONSTRAINT fk_favorites_tracks_track_id FOREIGN KEY (track_id) REFERENCES tracks (id) ON DELETE CASCADE,
    CONSTRAINT fk_favorites_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

CREATE INDEX ix_favorites_track_id ON favorites (track_id);

CREATE INDEX ix_favorites_user_id_created_at ON favorites (user_id, created_at);

CREATE TABLE listening_history (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    track_id uuid NOT NULL,
    played_at timestamp with time zone NOT NULL,
    playback_position integer NOT NULL,
    CONSTRAINT pk_listening_history PRIMARY KEY (id),
    CONSTRAINT fk_listening_history_tracks_track_id FOREIGN KEY (track_id) REFERENCES tracks (id) ON DELETE CASCADE,
    CONSTRAINT fk_listening_history_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

CREATE INDEX ix_listening_history_played_at ON listening_history (played_at);

CREATE INDEX ix_listening_history_track_id ON listening_history (track_id);

CREATE INDEX ix_listening_history_user_id_played_at ON listening_history (user_id, played_at);

CREATE INDEX ix_listening_history_user_id_track_id_played_at ON listening_history (user_id, track_id, played_at);
