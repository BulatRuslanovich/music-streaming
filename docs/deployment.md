# Deployment

One Docker Compose stack: PostgreSQL, the API, the frontend and a Caddy reverse proxy that gets its
own certificate. Everything below assumes a Linux host with Docker and the Compose plugin,
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

The upload page takes MP3, FLAC and M4A, checks each file against the library before sending it,
and reports what was skipped as a duplicate.

Expect the first minutes after a large upload to be busy: ffmpeg is building HLS variants and the
embedding worker is running the CLAP model over each track. `TRANSCODE_BACKFILL_PAUSE_SECONDS` is what keeps that work
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
a column it does not know about, while a new version fails on the first query that touches a
column the database does not have. The scripts in `db/init` describe the current schema and are only ever read by an
empty database, so editing them changes nothing on a running installation.

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

**Uploads fail at some size.** Two limits, and the outer one wins: `MAX_UPLOAD_BODY_BYTES` in Caddy,
`MAX_UPLOAD_BYTES` in the API. Keep the first comfortably above the second.

**Locked out of the owner account.** Set `OWNER_RESET_PASSWORD=true` with a new `OWNER_PASSWORD`,
`docker compose up -d backend`, then set it back to `false`. A lock from repeated failed sign-ins
clears itself after 15 minutes, or immediately on a restart.

**The disk filled up.** `du -sh storage/*`. `hls/` is derived and safe to delete
while the stack is down — the backfill rebuilds it. `storage/music` is not.
