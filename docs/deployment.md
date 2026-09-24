# Deployment

One Docker Compose stack: PostgreSQL, the API, the frontend and a Caddy reverse proxy that gets its
own certificate, plus an optional Prometheus/Loki/Grafana trio bound to localhost behind the
`observability` profile. Everything below assumes a Linux host with Docker and the Compose plugin,
and a DNS record pointing at it.

## First run

```bash
git clone https://github.com/BulatRuslanovich/music-streaming.git /srv/music-streaming
cd /srv/music-streaming
cp .env.example .env
```

Fill in the four values that have no default:

```bash
{
  echo "POSTGRES_PASSWORD=$(openssl rand -base64 48 | tr -d '\n')"
  echo "JWT_SIGNING_KEY=$(openssl rand -base64 48 | tr -d '\n')"
} >> .env

$EDITOR .env   # OWNER_PASSWORD and PUBLIC_DOMAIN by hand
```

`PUBLIC_DOMAIN` must already resolve to this host and ports 80/443 must reach it, or Caddy cannot
complete the certificate challenge. Then:

```bash
docker compose up -d
docker compose ps
```

The schema is not the API's job: `db/init` is mounted into the postgres container as
`/docker-entrypoint-initdb.d`, so the first start of an empty database builds it (see
[db/README.md](../db/README.md)). The API waits for that database, checks that it holds everything
this version needs and seeds the owner account from `OWNER_*`. Sign in at `https://<PUBLIC_DOMAIN>`
as `OWNER_USERNAME`.

Every other setting is documented in [configuration.md](configuration.md).

### Building the images yourself

The default is to pull from GHCR. To build from the working tree instead:

```bash
docker compose -f docker-compose.yml -f docker-compose.build.yml up -d --build
```

Published images target `linux/amd64` servers.

## Getting the music in

Two ways, and for an existing collection the second is the only practical one.

**Through the browser.** The upload page takes MP3, FLAC and M4A, checks each file against the
library before sending it, and reports what was skipped as a duplicate.

**Straight onto the server.** Copy the files into the import folder on the storage volume:

```bash
rsync -av --info=progress2 ~/music/ me@server:/srv/music-streaming/storage/import/
```

Every `LIBRARY_IMPORT_SCAN_INTERVAL_SECONDS` (5 minutes by default) the server picks up whatever has
settled there, reads the tags, and files it into the library; admins can also trigger a scan from the
upload page and watch it there. Nested folders are walked, non-audio files ignored, and anything that
cannot be read moves to `import/.failed` with a `.txt` explaining why. Imported originals are
deleted from the drop folder by default — the library already holds its own copy under
`storage/music` — so set `LIBRARY_IMPORT_AFTER=move` if you would rather they were archived under
`import/.imported`.

Expect the first minutes after a large import to be busy: ffmpeg is building HLS variants and the
analyzer is extracting audio features. `TRANSCODE_BACKFILL_PAUSE_SECONDS` is what keeps that work
from crowding out playback.

## Upgrading

```bash
cd /srv/music-streaming
git pull
scripts/deploy.sh          # or: scripts/deploy.sh 1.8.0 to pin a version
```

`deploy.sh` pulls the backend and frontend images, recreates what changed, and prints the resulting
status.

The database is upgraded separately, and by hand. A release that changes the schema says so and
gives the `ALTER` statements; run them **before** pulling the new images — an old version tolerates
a column it does not know about, a new version refuses to start on a database without one and names
what is missing. The scripts in `db/init` describe the current schema and are only ever read by an
empty database, so editing them changes nothing on a running installation.

## Backups

Copying the `postgres-data` volume while Postgres is running does not give you a backup — it gives
you files caught mid-write. Take a proper dump instead. The `backup` profile runs `pg_dump` on a
schedule into `BACKUP_PATH`, keeping the newest `BACKUP_KEEP` archives:

```bash
docker compose --profile backup up -d
```

It writes to a `.tmp` name and renames on success, so a dump interrupted halfway is never mistaken
for a good one, and a failed run is logged without stopping the schedule.

**Three things belong in your off-machine copy**, and only the first is in the dump:

| | Why |
| --- | --- |
| `BACKUP_PATH/*.dump` | accounts, playlists, favourites, listening history — nothing recomputes these |
| `<storage>/.dataprotection` | the keys that decrypt `lastfm_accounts.session_key`; restore the database without them and every Last.fm connection is dead |
| `MUSIC_STORAGE_PATH` | the audio itself. `storage/hls` and `storage/transcodes` are derived and rebuild themselves — skip them |

Taste profiles, affinities, similarity and embeddings are all recomputed from playback events, so
losing them costs time rather than data.

### Restoring

A backup nobody has restored is not a backup. Rehearse it into a throwaway database first:

```bash
docker compose exec postgres createdb -U music restore_test
docker compose exec postgres pg_restore -U music -d restore_test /backups/music-<stamp>.dump
docker compose exec postgres psql -U music -d restore_test -c 'SELECT count(*) FROM playlists;'
docker compose exec postgres dropdb -U music restore_test
```

For a real restore, stop the application first so nothing writes underneath it:

```bash
docker compose stop backend frontend
docker compose exec postgres dropdb -U music music
docker compose exec postgres createdb -U music music
docker compose exec postgres pg_restore -U music -d music /backups/music-<stamp>.dump
docker compose start backend frontend
```

`pg_restore` reports errors for objects it cannot recreate; read them rather than assuming the
count of restored rows is the whole story. `SchemaGuard` gives you a second opinion — the backend
refuses to start against a database missing anything the model expects, and names it.

## Monitoring

The monitoring stack — Prometheus, Grafana, Loki, Promtail and node-exporter — lives behind the
`observability` Compose profile, so `docker compose up -d` starts the application alone. Add
`GRAFANA_PASSWORD` to `.env` (`openssl rand -base64 24`) and start it with the profile:

```bash
docker compose --profile observability up -d
```

The same flag is needed for anything else aimed at those containers, `logs`, `ps` and `down`
included. To stop repeating it, put `COMPOSE_PROFILES=observability` in `.env` — every `docker
compose` command, `scripts/deploy.sh` included, then covers monitoring as well. Grafana refuses to
start with an empty `GRAFANA_PASSWORD` and says so in its log.

Prometheus scrapes `/metrics`, Promtail ships container logs to Loki, and Grafana is provisioned with
both plus two dashboards (`backend-health`, `recommendations`). Grafana listens on `127.0.0.1:3001`
only, so reach it over an SSH tunnel:

```bash
ssh -L 3001:127.0.0.1:3001 me@server
```

Alert rules live in [deploy/prometheus-alerts.yml](../deploy/prometheus-alerts.yml). Prometheus
evaluates them and shows the firing ones under Alerts; to have them delivered somewhere, point
Prometheus at an Alertmanager — that part is deliberately left to you, since where the alert should
land is a personal choice.

## Troubleshooting

**Caddy cannot get a certificate.** `docker compose logs caddy`. Almost always DNS not pointing here
yet, or ports 80/443 not reaching the host. Caddy retries on its own; nothing needs restarting.

**The backend keeps restarting.** `docker compose logs backend`. A configuration error names the key
it rejected — startup validation is deliberately loud. `Jwt:SigningKey must be at least 32 bytes` and
a missing `Owner:Password` on a fresh database are the common two.

**Everything plays at the original quality and never switches.** ffmpeg is missing from the image or
`TRANSCODE_ENABLED=false`; the HLS path then degrades to the original file by design. Check
`hlsEnabled` in `GET /api/config`.

**HLS variants never appear.** Look for the transcode worker in the logs. A backfill of a large
library takes hours on purpose — raise `TRANSCODE_BACKFILL_BATCH` and lower
`TRANSCODE_BACKFILL_PAUSE_SECONDS` if the machine is idle anyway.

**Files sit in `import/` and nothing happens.** They are younger than
`LIBRARY_IMPORT_MIN_AGE_SECONDS`, they are not `.mp3`/`.flac`/`.m4a`, or they are under a dot-folder,
which the scan skips. `GET /api/library/import` reports what is waiting.

**Uploads fail at some size.** Two limits, and the outer one wins: `MAX_UPLOAD_BODY_BYTES` in Caddy,
`MAX_UPLOAD_BYTES` in the API. Keep the first comfortably above the second.

**Locked out of the owner account.** Set `OWNER_RESET_PASSWORD=true` with a new `OWNER_PASSWORD`,
`docker compose up -d backend`, then set it back to `false`. A lock from repeated failed sign-ins
clears itself after `ACCOUNT_LOCKOUT_MINUTES`, or immediately on a restart.

**The disk filled up.** `du -sh storage/*`. `transcodes/` and `hls/` are derived and safe to delete
while the stack is down — the backfill rebuilds them. `storage/music` is not.
