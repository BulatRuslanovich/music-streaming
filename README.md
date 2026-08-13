<h1 align="center">Caimack</h1>

<p align="center">
  <b>A personal, self-hosted music streaming service for your own MP3 library.</b><br>
  Your files stay on your computer. Everything else works like a streaming app.
</p>

<p align="center">
  <a href="#quick-start">Quick start</a> ·
  <a href="#local-development">Local development</a> ·
  <a href="docs/SCREENSHOTS.md">Screenshots</a>
</p>

---

## Demo

<p align="center">
  <img src="docs/images/home.png" alt="Caimack home screen" width="900">
</p>

<p align="center">
  <img src="docs/images/album.png" alt="Album page" width="440">
  <img src="docs/images/player-fullscreen.png" alt="Full screen player" width="440">
</p>

<p align="center"><a href="docs/SCREENSHOTS.md"><b>See more screenshots →</b></a></p>

What you get: a browsable library of tracks, albums, artists and genres, playlists (private,
or public for everyone to listen to) and favourites, full-text search, a persistent player with a queue and a full-screen mode,
recommendation shelves built from your listening history, drag-and-drop uploads with tag
editing, and an admin area for users and metadata.

---

## Quick start

Requirements: Docker with Compose. Nothing else — .NET and Node only matter for local
development.

```bash
git clone <this repo> && cd music-streaming

cp .env.example .env
$EDITOR .env

docker compose up -d
```

Then open **https://your-domain** and sign in with the credentials you put in `.env`.

The sidebar footer shows the running version, commit and build time. The commit is baked into the
images at build time, so pass it in when you build:

```bash
GIT_SHA=$(git rev-parse --short HEAD) docker compose up -d --build
```

Without the variable everything still works — the footer just shows a dash instead of the hash.
Don't put `GIT_SHA` in `.env`: it would go stale on the very next commit.

Four values in `.env` are required and have no defaults:

| Variable | What to use |
| --- | --- |
| `POSTGRES_PASSWORD` | any long random string — `openssl rand -base64 48` |
| `JWT_SIGNING_KEY` | 32+ bytes of randomness — `openssl rand -base64 48` |
| `OWNER_PASSWORD` | your own password, 8+ characters |
| `PUBLIC_DOMAIN` | the hostname Caddy serves and requests a certificate for |

---

## Local development

Two terminals, with PostgreSQL in Docker.

The deployed stack deliberately keeps PostgreSQL off the host network, so local development
uses an override file that publishes it on loopback only:

```bash
docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d postgres

# terminal 1 — API on http://localhost:5199
cd backend/src/MusicStreaming.Api
dotnet run

# terminal 2 — frontend on http://localhost:3000
cd frontend
npm install
npm run dev
```

Tests:

```bash
cd backend
dotnet test                                  # unit tests, plus integration tests if Docker is up
dotnet test tests/MusicStreaming.UnitTests   # unit tests only — no Docker needed
```