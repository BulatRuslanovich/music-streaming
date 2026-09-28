# Database

Caimack's schema lives here, not in the application. The backend never creates, alters or checks
it: at startup it only waits for Postgres to accept connections (`DatabaseInitializer`).

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
| `050_track_signals.sql`          | track stats, embeddings, similarity                           |
| `060_taste_profiles.sql`         | playback events and the taste profile                         |
| `070_recommendation_serving.sql` | what the recommendation worker writes and the API serves      |

These files are the only description of indexes, lengths, nullability and constraints. The EF
configurations in `backend/src/MusicStreaming.Infrastructure/Persistence/Configurations` hold just
enough mapping for EF to read and write the tables, not a second copy of the schema.

## Changing the schema

There are no migrations. A change is two steps, and a person does both:

1. Edit the entity in the backend (and its `IEntityTypeConfiguration` only if EF cannot map the
   change by convention), then the file in `db/init` that owns that group of tables. These files describe the database as it should be **now**, not
   the path that led to it.
2. Apply the same change to the live database by hand (`ALTER TABLE ...`) — *before* the new version
   of the application reaches it. The order matters: an application on the old schema tolerates an
   extra column, while an application on the new schema fails on the first query that touches a
   column the old database does not have.

The integration tests build their database from these same scripts, so an `ALTER` you forgot in
`db/init` fails `make test-back` rather than production. A forgotten index fails nothing — search
simply gets slower as the library grows.

## Locally

The scripts run once, on an empty volume, so after editing `db/init` it is easier to recreate the
development database than to patch it:

```bash
make db-reset   # drops the postgres-data volume and brings the database back up
```
