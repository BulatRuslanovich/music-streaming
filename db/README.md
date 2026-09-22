# Database

Caimack's schema lives here, not in the application. The backend never creates or alters it: at
startup it waits for Postgres, compares the EF model against what it finds, and — if anything the
model expects is missing — names the missing tables and columns and refuses to run (`SchemaGuard`,
`DatabaseInitializer`).

## A new database

`db/init` is mounted into the postgres container as `/docker-entrypoint-initdb.d`, so the first
start of the stack creates the database by itself:

```bash
docker compose up -d postgres
```

The scripts run in alphabetical order and **only on an empty data directory**. Postgres leaves an
existing database alone, which means editing a file in `db/init` changes nothing in a running
database.

Postgres outside Docker — same files, same order:

```bash
for f in db/init/*.sql; do psql -v ON_ERROR_STOP=1 -d music -f "$f"; done
```

## The files

| File                             | What is in it                                               |
| -------------------------------- | ----------------------------------------------------------- |
| `001_extensions.sql`             | `pg_trgm` and the `search_rank` function                      |
| `010_users.sql`                  | accounts and sessions                                         |
| `020_library.sql`                | the catalogue: artists, albums, genres, tracks                |
| `030_listening.sql`              | listener settings, lyrics, hourly statistics                  |
| `040_user_content.sql`           | playlists, favourites, history                                |
| `050_track_signals.sql`          | track stats, audio features, embeddings, similarity           |
| `060_taste_profiles.sql`         | playback events and the taste profile                         |
| `070_recommendation_serving.sql` | what the recommendation worker writes and the API serves      |
| `080_integrations.sql`           | Last.fm and the outbound job queue                            |

The grouping mirrors the EF configurations in
`backend/src/MusicStreaming.Infrastructure/Persistence/Configurations`: every file here has a
namesake there, so the place for a new table is found by the entity's name.

## Changing the schema

There are no migrations. A change is two steps, and a person does both:

1. Edit the entity and its `IEntityTypeConfiguration` in the backend, then the file in `db/init`
   that owns that group of tables. These files describe the database as it should be **now**, not
   the path that led to it.
2. Apply the same change to the live database by hand (`ALTER TABLE ...`) — *before* the new version
   of the application reaches it. The order matters: an application on the old schema tolerates an
   extra column, while an application on the new schema will not start against the old database.

The backend tells you exactly what does not line up: it prints the list of missing objects at
startup. The integration tests build their database from these same scripts, so an `ALTER` you
forgot in `db/init` fails `make test-back` rather than production.

What `SchemaGuard` does **not** check: column types, nullability, indexes, defaults, foreign keys,
or the body of the `search_rank` function. It compares names only. A `gin_trgm_ops` index you
forgot will not fail startup — search will simply get slower as the library grows.

## Locally

The scripts run once, on an empty volume, so after editing `db/init` it is easier to recreate the
development database than to patch it:

```bash
make db-reset   # drops the postgres-data volume and brings the database back up
```

The backend does the checking itself at startup: `Database schema matches the model` in the log
means the EF model and what these scripts built agree.
