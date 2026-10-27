# Database

Caimack's schema is `db/migrations/0001_baseline.sql`. The file is compiled into the `caimack`
binary.

Only `caimack migrate` changes the schema. The server never does: at startup it compares the
migration recorded in the database with the one it was built with and refuses to start when they
differ, saying what to do.

## Docker Compose

The one-shot `migrate` service runs before `backend` on every `docker compose up`. When it has
nothing to apply it exits at once.

## Without Docker

```bash
DATABASE_URL=postgres://music:password@localhost:5432/music caimack migrate
```

In development `make migrate` (also part of `make backend`) applies the same file with sqlx-cli,
so it works even when the backend's queries don't compile against the current database yet.

## Changing the schema

Until the first release the schema is edited in place in `0001_baseline.sql`; there are no numbered
follow-up migrations. A database migrated from an older version of the file is then rejected as
changed, so bring it in line by hand:

1. Apply the matching `ALTER` statements to the dev database.
2. Record the new checksum:

   ```bash
   sum=$(sha384sum db/migrations/0001_baseline.sql | cut -d' ' -f1)
   psql "$DATABASE_URL" -c "UPDATE _sqlx_migrations SET checksum = decode('$sum', 'hex') WHERE version = 1"
   ```

3. Run `make sqlx`, so builds without a database (CI, Docker) see the new queries.

Or drop the dev database and run `make migrate` again, if its data doesn't matter.
