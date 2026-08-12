# Caimack — personal self-hosted music streaming

A private music streaming service for your own MP3 library. Files stay on your computer;

---

## Quick start

Requirements: Docker with Compose. Nothing else — .NET and Node only matter for local development.

```bash
git clone <this repo> && cd music-streaming

cp .env.example .env
$EDITOR .env

docker compose up -d
```

Then open **https://your-domain** and sign in with the credentials you put in `.env`.

Four values in `.env` are required and have no defaults:

| Variable | What to use |
| --- | --- |
| `POSTGRES_PASSWORD` | any long random string — `openssl rand -base64 48` |
| `JWT_SIGNING_KEY` | 32+ bytes of randomness — `openssl rand -base64 48` |
| `OWNER_PASSWORD` | your own password, 8+ characters |
| `PUBLIC_DOMAIN` | the hostname Caddy serves and requests a certificate for |


## Local development

Two terminals, with PostgreSQL in Docker.

The deployed stack deliberately keeps PostgreSQL off the host network, so local development uses
an override file that publishes it on loopback only:

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

The integration tests start a throwaway PostgreSQL in Docker. Without Docker they skip rather
than fail.


## Recommendations

The home page is personal: it learns from what you actually listen to, and gets better as it
accumulates evidence. Nothing about it needs a GPU, an external service or a model download — it
is PostgreSQL, a few background workers and roughly four hundred lines of arithmetic.

### How it works

```
player → POST /api/events → event log → taste profile → candidates → ranking → cached shelves
                                                                                     ↓
                                                          GET /api/recommendations/home
```

Every play reports how much of the track was actually heard, and that is the signal the engine
leans on hardest: finishing a track means far more than starting one, and abandoning it after five
seconds is a clear no. Likes, playlist additions and repeats count for more still, because they
cost the listener something.

The raw events are pruned after 180 days, but nothing is lost — they are folded into per-track,
per-artist and per-genre affinity as they arrive, with an exponential half-life so old habits fade
rather than accumulate forever.

Recommendations come from three layers that combine into one ranking:

- **content** — shared artists and albums, genre, release year, length;
- **collaborative** — tracks that get played together in one sitting, or filed together in a
  playlist, scored with a co-occurrence cosine and shrunk towards zero while the evidence is thin;
- **behaviour** — how much you like the artist and the genre in question.

The mix shifts as a profile matures: someone the engine knows nothing about is ranked almost
entirely on what is popular and new, and personal signals take over from there.

Cross-user collaborative filtering exists but stays switched off below five active listeners. With
two or three accounts, "listeners like you" describes one person, and the item-item similarity
built from listening sessions is the honest version of the same idea. It turns itself on when the
install grows — no migration, no configuration change.

Two rules keep the shelves from going stale: **variety**, which caps how many tracks one artist,
album or genre may take (the first thing that gives way when a small library cannot fill a shelf
otherwise), and **exploration**, which reserves a quarter of every shelf for music outside your
established taste — sixty per cent on the "you might like" shelf.

### API

```
POST   /api/events                         report playback events (batched, fire and forget)
GET    /api/recommendations/home           the personal home page, shelf by shelf
GET    /api/recommendations/tracks         the personalised feed, paged
GET    /api/recommendations/artists        recommended artists
GET    /api/recommendations/albums         recommended albums
GET    /api/recommendations/similar/{id}   tracks similar to one track
GET    /api/tracks/{id}/similar            the same, under the track's own route
GET    /api/admin/recommendations/stats    diagnostics (administrators only)
```

Shelves carry a reason as data — `{ kind: "becauseYouListened", subject: "…" }` — not a finished
sentence, so the interface renders it in whichever language it is being read in.

### Tuning

Defaults live in `backend/src/MusicStreaming.Api/appsettings.json` under `Recommendations`, and
the few worth reaching for are exposed in `.env`. Nothing needs changing to run.

Shelves rebuild in the background about a minute after you stop listening, and the library-wide
similarity model rebuilds every six hours. A brand-new account gets its shelves generated on its
first visit.

### Metrics

Prometheus and Grafana ship with the stack. Neither is exposed publicly: Prometheus publishes no
port at all, and Grafana binds to loopback, so on a server you reach it through a tunnel.

```bash
ssh -L 3001:127.0.0.1:3001 your-host
# then open http://localhost:3001 — the "Caimack — Recommendations" dashboard is preloaded
```

It shows cache hit ratio, generation time, candidate volume, and — the numbers that actually say
whether the recommendations are any good — how often they are clicked, and how often they are
skipped.