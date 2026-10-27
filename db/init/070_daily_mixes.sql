-- Дневной микс: список треков на местный день слушателя. Стабилен в течение дня, вчерашний
-- удаляется, когда собирается сегодняшний.

CREATE TABLE daily_mixes (
    user_id uuid NOT NULL,
    local_date date NOT NULL,
    track_ids jsonb NOT NULL,
    CONSTRAINT pk_daily_mixes PRIMARY KEY (user_id, local_date),
    CONSTRAINT fk_daily_mixes_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);
