# Caimack — personal self-hosted music streaming

A private music streaming service for your own MP3 library. Files stay on your computer;
PostgreSQL only holds metadata. Reachable from your LAN and, through a Cloudflare Tunnel, from
anywhere — without exposing your home network or forwarding a single router port.


---

## Quick start

Requirements: Docker with Compose. Nothing else — .NET and Node only matter for local development.

```bash
git clone <this repo> && cd music-streaming

cp .env.example .env
$EDITOR .env          # set the three required values, see below

docker compose up -d
```

Then open **http://localhost:8080** and sign in with the credentials you put in `.env`.

Three values in `.env` are required and have no defaults:

| Variable | What to use |
| --- | --- |
| `POSTGRES_PASSWORD` | any long random string — `openssl rand -base64 48` |
| `JWT_SIGNING_KEY` | 32+ bytes of randomness — `openssl rand -base64 48` |
| `OWNER_PASSWORD` | your own password, 8+ characters |

Also check `PUID`/`PGID`. The API container runs as that user so it can write to your music
directory; if `id -u` does not print `1000`, set them accordingly.

The account is created on first start. To change the password later, set `OWNER_PASSWORD` to the
new value plus `OWNER_RESET_PASSWORD=true`, start once, then set it back to `false`.

### Adding music

Go to **Upload**, drag in MP3 files. The server reads each file's ID3 tags and files it under the
right artist, album and genre, extracting embedded cover art as it goes. Nothing needs to be typed
in by hand; if a file's tags are missing or wrong, fix them afterwards from the track's ⋮ menu.

---

## Local development

Two terminals, with PostgreSQL in Docker.

The deployed stack deliberately keeps PostgreSQL off the host network, so local development uses
an override file that publishes it on loopback only:

```bash
# database on 127.0.0.1:5432 (needs .env for the password)
docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d postgres

# terminal 1 — API on http://localhost:5199
cd backend/src/MusicStreaming.Api
dotnet run

# terminal 2 — frontend on http://localhost:3000
cd frontend
npm install
npm run dev
```

Development settings live in `appsettings.Development.json`: connection string, a dev signing key
and the seed account (`admin` / whatever `Owner:Password` says). Keep the database password there
in step with `POSTGRES_PASSWORD` in `.env`.

Next.js rewrites `/api/*` to `http://localhost:5199` in development, which keeps the auth cookies
same-origin exactly as in production — that is why `launchSettings.json` pins the API to 5199.
Point it elsewhere with `BACKEND_INTERNAL_URL`.

```bash
cd backend  && dotnet build           # compile everything
cd frontend && npm run lint           # eslint
cd frontend && npm run build          # production build + typecheck
```

Schema changes:

```bash
cd backend
dotnet ef migrations add <Name> \
  --project src/MusicStreaming.Infrastructure \
  --startup-project src/MusicStreaming.Api \
  --output-dir Persistence/Migrations
```

Migrations are applied automatically on API start, retrying while PostgreSQL finishes booting.

