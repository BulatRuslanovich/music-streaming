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
    analyzed_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_track_cues PRIMARY KEY (track_id),
    CONSTRAINT fk_track_cues_tracks_track_id FOREIGN KEY (track_id) REFERENCES tracks (id) ON DELETE CASCADE
);

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
