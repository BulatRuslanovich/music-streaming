# Database

Caimack's schema lives here, not in the application. The backend never creates, alters or checks
it: at startup it only connects to Postgres.

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
