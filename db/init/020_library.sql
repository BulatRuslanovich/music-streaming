-- Каталог: исполнители, альбомы, жанры, треки (LibraryConfiguration).

CREATE TABLE artists (
    id uuid NOT NULL,
    name character varying(300) NOT NULL,
    normalized_name character varying(300) NOT NULL,
    image_path character varying(400),
    tags_fetched_at timestamp with time zone,
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
    file_path character varying(400) NOT NULL,
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
    added_by_user_id uuid,
    ingestion_source integer NOT NULL,
    tags_fetched_at timestamp with time zone,
    CONSTRAINT pk_tracks PRIMARY KEY (id),
    CONSTRAINT fk_tracks_albums_album_id FOREIGN KEY (album_id) REFERENCES albums (id) ON DELETE SET NULL,
    CONSTRAINT fk_tracks_artists_artist_id FOREIGN KEY (artist_id) REFERENCES artists (id) ON DELETE RESTRICT,
    CONSTRAINT fk_tracks_genres_genre_id FOREIGN KEY (genre_id) REFERENCES genres (id) ON DELETE SET NULL,
    CONSTRAINT fk_tracks_users_added_by_user_id FOREIGN KEY (added_by_user_id) REFERENCES users (id) ON DELETE SET NULL
);

CREATE INDEX ix_tracks_added_by_user_id_created_at ON tracks (added_by_user_id, created_at);

CREATE INDEX ix_tracks_album_id ON tracks (album_id);

CREATE INDEX ix_tracks_album_id_disc_number_track_number ON tracks (album_id, disc_number, track_number);

CREATE INDEX ix_tracks_artist_id ON tracks (artist_id);

CREATE INDEX ix_tracks_created_at ON tracks (created_at);

CREATE INDEX ix_tracks_genre_id ON tracks (genre_id);

CREATE INDEX ix_tracks_shuffle_key ON tracks (shuffle_key);

CREATE INDEX ix_tracks_title ON tracks (title);

CREATE UNIQUE INDEX ix_tracks_content_hash ON tracks (content_hash);

CREATE UNIQUE INDEX ix_tracks_file_path ON tracks (file_path);

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

-- Триграммные индексы стоят рядом с таблицами, по которым ищут: поиск идёт по
-- нормализованным полям, и без gin_trgm_ops запрос с LIKE '%...%' сползает в seq scan.
CREATE INDEX ix_artists_normalized_name_trgm ON artists USING gin (normalized_name gin_trgm_ops);

CREATE INDEX ix_albums_normalized_title_trgm ON albums USING gin (normalized_title gin_trgm_ops);

CREATE INDEX ix_tracks_normalized_title_trgm ON tracks USING gin (normalized_title gin_trgm_ops);

CREATE INDEX ix_genres_normalized_name_trgm ON genres USING gin (normalized_name gin_trgm_ops);
