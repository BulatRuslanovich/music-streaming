-- Всё, что известно о треке помимо тегов файла: статистика, аудиопризнаки, эмбеддинги,
-- похожесть и теги из внешних источников (TrackSignalConfiguration).

CREATE TABLE track_stats (
    track_id uuid NOT NULL,
    play_count integer NOT NULL,
    skip_count integer NOT NULL,
    skip_rate double precision NOT NULL,
    popularity_score double precision NOT NULL,
    -- Счётчики показов ведёт не тот запрос, что пересчитывает статистику:
    -- refresh-track-stats.sql вставляет строку без них и рассчитывает на эти значения.
    shown_count integer NOT NULL DEFAULT 0,
    skipped_early_count integer NOT NULL DEFAULT 0,
    last_played_at timestamp with time zone,
    computed_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_track_stats PRIMARY KEY (track_id),
    CONSTRAINT fk_track_stats_tracks_track_id FOREIGN KEY (track_id) REFERENCES tracks (id) ON DELETE CASCADE
);

CREATE INDEX ix_track_stats_popularity_score ON track_stats (popularity_score);

CREATE TABLE track_audio_features (
    track_id uuid NOT NULL,
    tempo_bpm double precision,
    tempo_confidence double precision NOT NULL,
    energy double precision NOT NULL,
    loudness_db double precision NOT NULL,
    brightness double precision NOT NULL,
    dynamic_range_db double precision NOT NULL,
    key integer,
    is_minor boolean NOT NULL,
    key_strength double precision NOT NULL,
    algorithm_version integer NOT NULL,
    succeeded boolean NOT NULL,
    error character varying(512),
    analyzed_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_track_audio_features PRIMARY KEY (track_id),
    CONSTRAINT fk_track_audio_features_tracks_track_id FOREIGN KEY (track_id) REFERENCES tracks (id) ON DELETE CASCADE
);

CREATE INDEX ix_track_audio_features_analyzed_at ON track_audio_features (analyzed_at);

CREATE INDEX ix_track_audio_features_succeeded_algorithm_version ON track_audio_features (succeeded, algorithm_version);

CREATE TABLE track_embeddings (
    track_id uuid NOT NULL,
    vector real[] NOT NULL,
    dimension integer NOT NULL,
    model_id character varying(64) NOT NULL,
    strategy character varying(32) NOT NULL,
    source_hash character varying(64) NOT NULL,
    cluster_id integer,
    succeeded boolean NOT NULL,
    error character varying(512),
    analyzed_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_track_embeddings PRIMARY KEY (track_id),
    CONSTRAINT fk_track_embeddings_tracks_track_id FOREIGN KEY (track_id) REFERENCES tracks (id) ON DELETE CASCADE
);

CREATE INDEX ix_track_embeddings_analyzed_at ON track_embeddings (analyzed_at);

CREATE INDEX ix_track_embeddings_cluster_id ON track_embeddings (cluster_id);

CREATE INDEX ix_track_embeddings_succeeded_model_id_strategy ON track_embeddings (succeeded, model_id, strategy);

CREATE TABLE track_similarity (
    track_id uuid NOT NULL,
    similar_track_id uuid NOT NULL,
    score double precision NOT NULL,
    content_score double precision NOT NULL,
    collab_score double precision NOT NULL,
    support integer NOT NULL,
    computed_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_track_similarity PRIMARY KEY (track_id, similar_track_id),
    CONSTRAINT fk_track_similarity_tracks_similar_track_id FOREIGN KEY (similar_track_id) REFERENCES tracks (id) ON DELETE CASCADE,
    CONSTRAINT fk_track_similarity_tracks_track_id FOREIGN KEY (track_id) REFERENCES tracks (id) ON DELETE CASCADE
);

CREATE INDEX ix_track_similarity_similar_track_id ON track_similarity (similar_track_id);

CREATE INDEX ix_track_similarity_track_id_score ON track_similarity (track_id, score);

CREATE TABLE track_similarity_state (
    track_id uuid NOT NULL,
    fingerprint character varying(32) NOT NULL,
    computed_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_track_similarity_state PRIMARY KEY (track_id),
    CONSTRAINT fk_track_similarity_state_tracks_track_id FOREIGN KEY (track_id) REFERENCES tracks (id) ON DELETE CASCADE
);

CREATE INDEX ix_track_similarity_state_computed_at ON track_similarity_state (computed_at);

CREATE TABLE artist_tags (
    artist_id uuid NOT NULL,
    name character varying(100) NOT NULL,
    weight double precision NOT NULL,
    CONSTRAINT pk_artist_tags PRIMARY KEY (artist_id, name),
    CONSTRAINT fk_artist_tags_artists_artist_id FOREIGN KEY (artist_id) REFERENCES artists (id) ON DELETE CASCADE
);

CREATE INDEX ix_artist_tags_name ON artist_tags (name);

CREATE TABLE track_tags (
    track_id uuid NOT NULL,
    name character varying(100) NOT NULL,
    weight double precision NOT NULL,
    CONSTRAINT pk_track_tags PRIMARY KEY (track_id, name),
    CONSTRAINT fk_track_tags_tracks_track_id FOREIGN KEY (track_id) REFERENCES tracks (id) ON DELETE CASCADE
);

CREATE INDEX ix_track_tags_name ON track_tags (name);

-- Вектор — массив float на несколько килобайт. TOAST по умолчанию пытается его сжать,
-- а на нормализованных float32 сжатие не даёт ничего и стоит процессора на каждой записи.
ALTER TABLE track_embeddings ALTER COLUMN vector SET STORAGE EXTERNAL;

CREATE TABLE track_transitions (
    from_track_id uuid NOT NULL,
    to_track_id uuid NOT NULL,
    weight double precision NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_track_transitions PRIMARY KEY (from_track_id, to_track_id),
    CONSTRAINT fk_track_transitions_tracks_from_track_id FOREIGN KEY (from_track_id) REFERENCES tracks (id) ON DELETE CASCADE,
    CONSTRAINT fk_track_transitions_tracks_to_track_id FOREIGN KEY (to_track_id) REFERENCES tracks (id) ON DELETE CASCADE
);

CREATE INDEX ix_track_transitions_to_track_id ON track_transitions (to_track_id);
