# Database

Caimack's schema lives in `db/migrations` as plain SQL, one numbered file per change
(`0001_baseline.sql`, `0002_…`). The files are compiled into the `caimack` binary.

Only `caimack migrate` changes the schema. The server never does: at startup it compares the
migrations recorded in the database with the ones it was built with and refuses to start when they
differ, saying what to do.

## Docker Compose

The one-shot `migrate` service runs before `backend` on every `docker compose up`. When it has
nothing to apply it exits at once, so updating is just:

```bash
git pull
docker compose up -d --build
```

## Without Docker

```bash
DATABASE_URL=postgres://music:password@localhost:5432/music caimack migrate
```

In development `make migrate` (also part of `make backend`) does the same against the dev
database.

## Databases created from `db/init`

Before migrations existed, the schema was created by `db/init` scripts on the first start of the
postgres container. Such a database already has everything in `0001_baseline.sql`, so the first
`caimack migrate` records the baseline as applied without running it, then applies the newer
migrations. It refuses if some of the baseline tables are missing.

## Writing a migration

- Add `db/migrations/NNNN_short_description.sql` with the next number.
- Never edit a migration that has been released: the server rejects a database whose applied
  migration differs from the one in the binary. Write a new one instead.
- After changing the schema, run `make migrate` and then `make sqlx`, so builds without a database
  (CI, Docker) see the new queries.
