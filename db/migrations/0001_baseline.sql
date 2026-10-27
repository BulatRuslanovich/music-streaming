-- Расширения и функции, на которые опираются остальные файлы.
--
-- search_rank — ранг совпадения для поиска (catalog::search): сортировка выдачи поиска
-- считается в базе, чтобы не тащить всю выборку в память ради одного ORDER BY.

CREATE EXTENSION IF NOT EXISTS pg_trgm;

CREATE OR REPLACE FUNCTION search_rank(value text, term text) RETURNS integer AS $$
    SELECT CASE
        WHEN value = term                                    THEN 0
        WHEN starts_with(value, term)                        THEN 1
        WHEN position(' ' || term in ' ' || value) > 0       THEN 2
        WHEN position(term in value) > 0                     THEN 3
        ELSE 4
    END;
$$ LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE;


-- Учётные записи и сессии.

CREATE TABLE users (
    id uuid NOT NULL,
    username character varying(100) NOT NULL,
    password_hash character varying(255) NOT NULL,
    is_admin boolean NOT NULL DEFAULT FALSE,
    is_active boolean NOT NULL DEFAULT TRUE,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_users PRIMARY KEY (id)
);

CREATE UNIQUE INDEX ix_users_username ON users (username);

CREATE TABLE refresh_tokens (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    token_hash character varying(64) NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    created_at timestamp with time zone NOT NULL,
    revoked_at timestamp with time zone,
    session_id uuid NOT NULL,
    device_name character varying(100),
    user_agent character varying(400),
    ip character varying(64),
    long_lived boolean NOT NULL,
    signed_in_at timestamp with time zone NOT NULL,
    last_used_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_refresh_tokens PRIMARY KEY (id),
    CONSTRAINT fk_refresh_tokens_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

CREATE INDEX ix_refresh_tokens_user_id ON refresh_tokens (user_id);

CREATE UNIQUE INDEX ix_refresh_tokens_token_hash ON refresh_tokens (token_hash);

CREATE INDEX ix_refresh_tokens_session_id ON refresh_tokens (session_id);

-- Каталог: исполнители, альбомы, жанры, треки.

CREATE TABLE artists (
    id uuid NOT NULL,
    name character varying(300) NOT NULL,
    normalized_name character varying(300) NOT NULL,
    image_path character varying(400),
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_artists PRIMARY KEY (id)
);

CREATE INDEX ix_artists_name ON artists (name);

CREATE UNIQUE INDEX ix_artists_normalized_name ON artists (normalized_name);

CREATE TABLE genres (
    id uuid NOT NULL,
    name character varying(150) NOT NULL,
    normalized_name character varying(150) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_genres PRIMARY KEY (id)
);

CREATE UNIQUE INDEX ix_genres_normalized_name ON genres (normalized_name);

CREATE TABLE albums (
    id uuid NOT NULL,
    title character varying(300) NOT NULL,
    normalized_title character varying(300) NOT NULL,
    artist_id uuid NOT NULL,
    year integer,
    cover_path character varying(400),
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_albums PRIMARY KEY (id),
    CONSTRAINT fk_albums_artists_artist_id FOREIGN KEY (artist_id) REFERENCES artists (id) ON DELETE RESTRICT
);

CREATE INDEX ix_albums_created_at ON albums (created_at);

CREATE INDEX ix_albums_title ON albums (title);

CREATE UNIQUE INDEX ix_albums_artist_id_normalized_title ON albums (artist_id, normalized_title);

CREATE TABLE tracks (
    id uuid NOT NULL,
    title character varying(400) NOT NULL,
    normalized_title character varying(400) NOT NULL,
    artist_id uuid NOT NULL,
    album_id uuid,
    genre_id uuid,
    track_number integer,
    disc_number integer,
    year integer,
    duration_seconds integer NOT NULL,
    file_path text NOT NULL,
    original_file_name character varying(400) NOT NULL,
    mime_type character varying(100) NOT NULL,
    file_size bigint NOT NULL,
    content_hash character varying(64) NOT NULL,
    codec character varying(16),
    bitrate_kbps integer,
    sample_rate_hz integer,
    bits_per_sample integer,
    shuffle_key double precision NOT NULL DEFAULT (random()),
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_tracks PRIMARY KEY (id),
    CONSTRAINT fk_tracks_albums_album_id FOREIGN KEY (album_id) REFERENCES albums (id) ON DELETE SET NULL,
    CONSTRAINT fk_tracks_artists_artist_id FOREIGN KEY (artist_id) REFERENCES artists (id) ON DELETE RESTRICT,
    CONSTRAINT fk_tracks_genres_genre_id FOREIGN KEY (genre_id) REFERENCES genres (id) ON DELETE SET NULL
);

CREATE INDEX ix_tracks_album_id_disc_number_track_number ON tracks (album_id, disc_number, track_number);

CREATE INDEX ix_tracks_artist_id ON tracks (artist_id);

CREATE INDEX ix_tracks_created_at ON tracks (created_at);

CREATE INDEX ix_tracks_genre_id ON tracks (genre_id);

CREATE INDEX ix_tracks_shuffle_key ON tracks (shuffle_key);

CREATE INDEX ix_tracks_title ON tracks (title);

CREATE UNIQUE INDEX ix_tracks_content_hash ON tracks (content_hash);

CREATE UNIQUE INDEX ix_tracks_file_path ON tracks (file_path);

CREATE TABLE library_files (
    path text NOT NULL,
    size_bytes bigint NOT NULL,
    modified_at timestamp with time zone NOT NULL,
    content_hash character varying(64),
    track_id uuid,
    status smallint NOT NULL,
    problem character varying(300),
    missing_since timestamp with time zone,
    scanned_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_library_files PRIMARY KEY (path),
    CONSTRAINT fk_library_files_tracks_track_id FOREIGN KEY (track_id) REFERENCES tracks (id) ON DELETE SET NULL
);

CREATE INDEX ix_library_files_track_id ON library_files (track_id);

CREATE INDEX ix_library_files_content_hash ON library_files (content_hash);

CREATE TABLE track_artists (
    track_id uuid NOT NULL,
    artist_id uuid NOT NULL,
    position integer NOT NULL,
    CONSTRAINT pk_track_artists PRIMARY KEY (track_id, artist_id),
    CONSTRAINT fk_track_artists_artists_artist_id FOREIGN KEY (artist_id) REFERENCES artists (id) ON DELETE RESTRICT,
    CONSTRAINT fk_track_artists_tracks_track_id FOREIGN KEY (track_id) REFERENCES tracks (id) ON DELETE CASCADE
);

CREATE INDEX ix_track_artists_artist_id ON track_artists (artist_id);

CREATE INDEX ix_track_artists_track_id_position ON track_artists (track_id, position);

CREATE INDEX ix_artists_normalized_name_trgm ON artists USING gin (normalized_name gin_trgm_ops);

CREATE INDEX ix_albums_normalized_title_trgm ON albums USING gin (normalized_title gin_trgm_ops);

CREATE INDEX ix_tracks_normalized_title_trgm ON tracks USING gin (normalized_title gin_trgm_ops);

CREATE INDEX ix_genres_normalized_name_trgm ON genres USING gin (normalized_name gin_trgm_ops);

-- Настройки слушателя и тексты песен.

CREATE TABLE user_settings (
    user_id uuid NOT NULL,
    quality integer NOT NULL,
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

-- То, что заводит сам слушатель: плейлисты, избранное, история.

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

CREATE INDEX ix_listening_history_track_id ON listening_history (track_id);

CREATE INDEX ix_listening_history_user_id_played_at ON listening_history (user_id, played_at);

CREATE INDEX ix_listening_history_user_id_track_id_played_at ON listening_history (user_id, track_id, played_at);

-- Всё, что известно о треке помимо тегов файла: статистика, эмбеддинги, точки склейки и пары
-- «не дубликат».

CREATE TABLE track_stats (
    track_id uuid NOT NULL,
    play_count integer NOT NULL,
    popularity_score double precision NOT NULL,
    skipped_early_count integer NOT NULL,
    CONSTRAINT pk_track_stats PRIMARY KEY (track_id),
    CONSTRAINT fk_track_stats_tracks_track_id FOREIGN KEY (track_id) REFERENCES tracks (id) ON DELETE CASCADE
);

CREATE INDEX ix_track_stats_popularity_score ON track_stats (popularity_score);

CREATE TABLE track_embeddings (
    track_id uuid NOT NULL,
    vector real[] NOT NULL,
    dimension integer NOT NULL,
    model_id character varying(64) NOT NULL,
    strategy character varying(32) NOT NULL,
    source_hash character varying(64) NOT NULL,
    succeeded boolean NOT NULL,
    error character varying(512),
    analyzed_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_track_embeddings PRIMARY KEY (track_id),
    CONSTRAINT fk_track_embeddings_tracks_track_id FOREIGN KEY (track_id) REFERENCES tracks (id) ON DELETE CASCADE
);

CREATE INDEX ix_track_embeddings_analyzed_at ON track_embeddings (analyzed_at);

CREATE INDEX ix_track_embeddings_succeeded_model_id_strategy ON track_embeddings (succeeded, model_id, strategy);

ALTER TABLE track_embeddings ALTER COLUMN vector SET STORAGE EXTERNAL;

-- Точки склейки для плееров: где звук начинается и кончается без тишины по краям, где начинается
-- затухание, какой темп и как выглядит волна (анализ трека). version — версия анализатора: её смена пересчитывает всё.
CREATE TABLE track_cues (
    track_id uuid NOT NULL,
    source_hash character varying(64) NOT NULL,
    version integer NOT NULL,
    succeeded boolean NOT NULL,
    audio_start_seconds double precision,
    audio_end_seconds double precision,
    outro_start_seconds double precision,
    tempo_bpm double precision,
    waveform bytea,
    loudness_lufs double precision,
    true_peak_db double precision,
    loudness_range_lu double precision,
    musical_key smallint,
    key_confidence real,
    analyzed_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_track_cues PRIMARY KEY (track_id),
    CONSTRAINT fk_track_cues_tracks_track_id FOREIGN KEY (track_id) REFERENCES tracks (id) ON DELETE CASCADE,
    CONSTRAINT ck_track_cues_musical_key CHECK (musical_key BETWEEN 0 AND 23)
);

CREATE TABLE track_fingerprints (
    track_id uuid NOT NULL,
    fingerprint integer[] NOT NULL,
    CONSTRAINT pk_track_fingerprints PRIMARY KEY (track_id),
    CONSTRAINT fk_track_fingerprints_tracks_track_id FOREIGN KEY (track_id) REFERENCES tracks (id) ON DELETE CASCADE
);

ALTER TABLE track_fingerprints ALTER COLUMN fingerprint SET STORAGE EXTERNAL;

-- Пары, которые админ отметил «не дубликат»; first_track_id < second_track_id.
CREATE TABLE duplicate_dismissals (
    first_track_id uuid NOT NULL,
    second_track_id uuid NOT NULL,
    dismissed_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_duplicate_dismissals PRIMARY KEY (first_track_id, second_track_id),
    CONSTRAINT fk_duplicate_dismissals_tracks_first_track_id FOREIGN KEY (first_track_id) REFERENCES tracks (id) ON DELETE CASCADE,
    CONSTRAINT fk_duplicate_dismissals_tracks_second_track_id FOREIGN KEY (second_track_id) REFERENCES tracks (id) ON DELETE CASCADE
);

CREATE INDEX ix_duplicate_dismissals_second_track_id ON duplicate_dismissals (second_track_id);

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

-- Дневной микс: список треков на местный день слушателя. Стабилен в течение дня, вчерашний
-- удаляется, когда собирается сегодняшний.

CREATE TABLE daily_mixes (
    user_id uuid NOT NULL,
    local_date date NOT NULL,
    track_ids jsonb NOT NULL,
    CONSTRAINT pk_daily_mixes PRIMARY KEY (user_id, local_date),
    CONSTRAINT fk_daily_mixes_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

CREATE TABLE recommendation_impressions (
    id uuid NOT NULL,
    request_id uuid NOT NULL,
    user_id uuid NOT NULL,
    track_id uuid NOT NULL,
    source character varying(64) NOT NULL,
    position smallint NOT NULL,
    explore boolean NOT NULL,
    score real NOT NULL,
    features jsonb NOT NULL,
    created_at timestamp with time zone NOT NULL,
    outcome smallint,
    resolved_at timestamp with time zone,
    CONSTRAINT pk_recommendation_impressions PRIMARY KEY (id),
    CONSTRAINT fk_recommendation_impressions_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE,
    CONSTRAINT fk_recommendation_impressions_tracks_track_id FOREIGN KEY (track_id) REFERENCES tracks (id) ON DELETE CASCADE
);

CREATE INDEX ix_recommendation_impressions_user_id_created_at ON recommendation_impressions (user_id, created_at);

CREATE INDEX ix_recommendation_impressions_track_id ON recommendation_impressions (track_id);

CREATE INDEX ix_recommendation_impressions_unresolved ON recommendation_impressions (created_at) WHERE outcome IS NULL;

CREATE TABLE taste_rules (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    action smallint NOT NULL,
    target_type smallint NOT NULL,
    target_id uuid NOT NULL,
    until timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_taste_rules PRIMARY KEY (id),
    CONSTRAINT fk_taste_rules_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX ix_taste_rules_user_id_target_type_target_id ON taste_rules (user_id, target_type, target_id);

CREATE TABLE invitations (
    id uuid NOT NULL,
    token_hash character varying(64) NOT NULL,
    created_by uuid,
    is_admin boolean NOT NULL,
    created_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    used_at timestamp with time zone,
    used_by uuid,
    CONSTRAINT pk_invitations PRIMARY KEY (id),
    CONSTRAINT fk_invitations_users_created_by FOREIGN KEY (created_by) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_invitations_users_used_by FOREIGN KEY (used_by) REFERENCES users (id) ON DELETE SET NULL
);

CREATE UNIQUE INDEX ix_invitations_token_hash ON invitations (token_hash);

CREATE TABLE user_identities (
    issuer character varying(400) NOT NULL,
    subject character varying(255) NOT NULL,
    user_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_user_identities PRIMARY KEY (issuer, subject),
    CONSTRAINT fk_user_identities_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

CREATE INDEX ix_user_identities_user_id ON user_identities (user_id);

CREATE TABLE auth_events (
    id uuid NOT NULL,
    user_id uuid,
    username character varying(100) NOT NULL,
    method smallint NOT NULL,
    success boolean NOT NULL,
    reason character varying(64),
    ip character varying(64),
    user_agent character varying(400),
    device_name character varying(100),
    occurred_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_auth_events PRIMARY KEY (id),
    CONSTRAINT fk_auth_events_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

CREATE INDEX ix_auth_events_user_id_occurred_at ON auth_events (user_id, occurred_at);

CREATE INDEX ix_auth_events_username_occurred_at ON auth_events (username, occurred_at);
