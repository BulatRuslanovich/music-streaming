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

## Remote access with Cloudflare Tunnel

The tunnel dials **out** to Cloudflare, so there is no port forwarding, no dynamic DNS and no
inbound firewall rule. Your home IP is never published.

1. In the [Cloudflare Zero Trust dashboard](https://one.dash.cloudflare.com/), go to
   **Networks → Tunnels → Create a tunnel**, pick **Cloudflared**, and name it.
2. Copy the tunnel token (the long string in the install command Cloudflare shows you).
3. Add a **public hostname** to the tunnel:
   - *Subdomain / domain*: e.g. `music` / `example.com`
   - *Service type*: `HTTP`
   - *URL*: `caddy:80` — the tunnel container reaches Caddy over the internal Docker network.
4. Put the token in `.env` as `CLOUDFLARE_TUNNEL_TOKEN`, then start the tunnel profile:

```bash
docker compose --profile tunnel up -d
```

`https://music.example.com` now serves your library over HTTPS from anywhere — home Wi-Fi, mobile
data, someone else's network. Cloudflare handles the certificate.

To make the service reachable *only* through the tunnel, set `LOCAL_BIND_ADDRESS=127.0.0.1`.

> Worth doing: in Zero Trust, add an **Access policy** on the hostname so Cloudflare requires
> your identity before a request even reaches your house. The app's own login stays in place
> either way.

---

## How it works

### The library

MP3s are written to a sharded path under the storage root and never enter the database:

```text
/storage/
  music/8a/02/019fece513a873d19845e322621e028a.mp3
  covers/019fece5148878f098ac1afebf0ecb6b.jpg
```

Names are server-generated GUIDs; the original filename is kept in PostgreSQL as metadata only.
The two-level shard comes from the *random* tail of the GUID rather than its head — a version 7
GUID starts with a timestamp, so sharding on the front would drop every upload into the same
directory.

The database holds `users`, `artists`, `albums`, `genres`, `tracks`, `playlists`,
`playlist_tracks`, `favorites`, `listening_history` and `refresh_tokens`, with indexes on the
foreign keys and on the columns actually sorted or filtered (`title`, `created_at`, `played_at`).

### Streaming and seeking

`GET /api/tracks/{id}/stream` answers HTTP range requests: `206 Partial Content`,
`Accept-Ranges: bytes` and a `Content-Range` for each window the player asks for. Bytes are copied
from a `FileStream` — a 20 MB track never lands in server memory. The response carries an `ETag`
derived from the file's content hash, so a re-listen is served from browser cache.

Caddy proxies `/api/*` straight to the API with response buffering off, so audio never passes
through Node.

### Authentication

A JWT in an **HttpOnly cookie**, not in `localStorage`. That is not a stylistic choice: an
`<audio>` element cannot attach an `Authorization` header to its own request, so a token held in
JavaScript could never protect the streaming endpoint. The cookie rides along automatically,
and being HttpOnly it is also out of reach of injected script.

This is why Caddy serves the app and the API from **one origin** with the API under `/api` — the
cookie has to apply to both. `SameSite=Lax` does the CSRF work: the cookie is not sent on
cross-site POST/PUT/DELETE requests.

Refresh tokens are opaque random values, stored only as SHA-256 digests, and single-use: each
refresh revokes the one it replaces, so a stolen token stops working as soon as the real client
refreshes. Login is rate-limited to 10 attempts per minute per IP, and a wrong username costs the
same time as a wrong password.

### Layering

```text
MusicStreaming.Api             controllers, auth, HTTP concerns
MusicStreaming.Application     services, DTOs, use cases    ← the business logic lives here
MusicStreaming.Infrastructure  EF Core, filesystem, JWT, ID3
MusicStreaming.Domain          entities
```

Controllers only translate HTTP to a service call. Dependencies point inwards: `Application`
defines the interfaces (`IMusicStorage`, `IAudioMetadataReader`, `ITokenService`) that
`Infrastructure` implements.

---

## Everyday commands

```bash
docker compose up -d                      # start
docker compose logs -f backend            # follow API logs
docker compose down                       # stop (your music and database are untouched)
docker compose up -d --build              # apply code changes
docker compose --profile tunnel up -d     # start with remote access
```

### Backups

The database and the music files are backed up separately — the database is small and changes
constantly, the audio is large and effectively append-only:

```bash
./deploy/backup.sh              # database + config, and mirror the music tree
./deploy/backup.sh --db-only    # database + config only
```

Snapshots land in `./backups/<timestamp>/` (override with `BACKUP_PATH`), keeping the last 14 by
default (`BACKUP_KEEP`). The music mirror is a single rsync target reused across runs, so history
does not multiply a large library. `pg_dump` does not lock readers and the API opens audio files
with shared read access, so a backup never interrupts playback. The script prints the exact
restore commands when it finishes.

A cron entry for nightly backups:

```cron
30 4 * * * cd /path/to/music-streaming && ./deploy/backup.sh >> /var/log/music-backup.log 2>&1
```

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

---

## API

Everything except `login`, `refresh` and `/health` requires authentication.

| Method | Path | Purpose |
| --- | --- | --- |
| `POST` | `/api/auth/login` | sign in; sets the auth cookies |
| `POST` | `/api/auth/refresh` | rotate tokens |
| `POST` | `/api/auth/logout` | revoke the session |
| `GET` | `/api/auth/me` | current user |
| `GET` | `/api/home` | everything the home page shows, in one request |
| `GET` | `/api/tracks` | paged list; `?sort=Title\|Recent\|Artist\|Album` |
| `GET` | `/api/tracks/{id}` | one track |
| `GET` | `/api/tracks/{id}/stream` | audio, with range support |
| `GET` | `/api/tracks/{id}/cover` | cover art |
| `POST` | `/api/tracks/upload` | multipart upload, field `files` |
| `PUT` | `/api/tracks/{id}` | correct metadata |
| `DELETE` | `/api/tracks/{id}` | delete track and its file |
| `POST`/`DELETE` | `/api/tracks/{id}/favorite` | favourite / unfavourite |
| `GET` | `/api/favorites` | favourites |
| `GET` | `/api/artists`, `/api/artists/{id}` | artists |
| `GET` | `/api/albums`, `/api/albums/{id}` | albums |
| `GET` | `/api/albums/{id}/cover` | album art |
| `GET` | `/api/genres`, `/api/genres/{id}/tracks` | genres |
| `GET` | `/api/search?q=` | grouped results: artists, albums, tracks, genres |
| `GET`/`POST` | `/api/playlists` | list / create |
| `GET`/`PUT`/`DELETE` | `/api/playlists/{id}` | read / rename / delete |
| `POST` | `/api/playlists/{id}/tracks` | add a track |
| `DELETE` | `/api/playlists/{id}/tracks/{trackId}` | remove a track |
| `PUT` | `/api/playlists/{id}/tracks/order` | reorder |
| `GET`/`POST`/`DELETE` | `/api/history` | play log / record a play / clear |
| `GET` | `/api/history/recent` | distinct tracks, most recent play first |
| `GET` | `/api/config` | client settings (history threshold, upload limit) |
| `GET` | `/health` | liveness, unauthenticated |

Failures come back as RFC 7807 problem responses with a human-readable `detail`.

---

## Player notes

The player lives in the root layout, so navigating never interrupts playback. It keeps a queue
with shuffle and repeat (off / all / one), and shuffling permutes a separate order array rather
than the queue itself — turning it off restores the original order without reloading.

Queue, position, volume and modes are saved to `localStorage`, so closing the browser and coming
back later resumes where you left off, paused. Media keys and the phone lock screen work through
the Media Session API. On the desktop, <kbd>Space</kbd> toggles playback and
<kbd>Shift</kbd>+<kbd>←</kbd>/<kbd>→</kbd> change track.

A play is recorded once you have listened for `HISTORY_THRESHOLD_SECONDS` (default 30). The
frontend reads that number from `/api/config` rather than hard-coding it.

On phones the sidebar becomes a bottom tab bar and tapping the artwork opens a full-screen player
with large touch targets.

---

## Security

- No part of the library is public: authentication is the default for every endpoint, and the few
  open ones opt out explicitly.
- Streaming is authenticated too — an unauthenticated `GET` on a stream URL returns 401.
- Uploads are checked on extension, MIME type and size, and validated by actually parsing the
  MP3; a renamed non-audio file is rejected and its bytes deleted.
- The size limit is enforced *during* the copy, so an oversized upload cannot fill the disk first.
- Clients never supply filesystem paths. Every path is server-generated and re-validated against
  the storage root before it reaches the filesystem, so `..` and absolute paths cannot escape.
- The storage directory is not served by the web server; audio only comes through the
  authenticated API.
- Passwords are hashed with BCrypt at work factor 12.
- Only Caddy publishes a port; the API and PostgreSQL are unreachable from the host network.

## Not in this version

Recommendations, AI playlists, multiple users, transcoding, FLAC/AAC/OGG, offline downloads,
sharing, native apps. The MVP is deliberately about reliable playback of your own MP3s.
