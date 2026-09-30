<h1 align="center">Caimack</h1>

<p align="center"><strong>Your music library, streamed from your own server.</strong></p>

<p align="center">Self-hosted · Private · Built for everyday listening</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white" alt=".NET 10">
  <img src="https://img.shields.io/badge/Next.js-16-000000?logo=next.js&logoColor=white" alt="Next.js 16">
  <img src="https://img.shields.io/badge/Docker-Compose-2496ED?logo=docker&logoColor=white" alt="Docker Compose">
  <img src="https://img.shields.io/badge/License-MIT-green" alt="MIT license">
</p>

Caimack turns a personal music collection into a private streaming service. Upload your library,
invite the people you trust, and listen from any browser without giving your collection or listening
history to another platform.


## Highlights

- **Reliable on changing mobile networks.** Adaptive HLS switches between 64, 128 and 192 kbps,
  while a bounded cache prepares the current track and the next two for patchy coverage.
- **Recommendations from your own listening.** Daily mixes, radio and discovery shelves learn from
  real plays while keeping the data on your server.
- **A complete music player.** Synced lyrics, an editable queue, shuffle, repeat, media keys and
  lock-screen controls work together across desktop and mobile.
- **A library you control.** Upload MP3, FLAC and M4A files, organize albums and playlists, search the
  whole collection and download original files whenever you need them.
- **Simple private hosting.** The application, database and HTTPS proxy run from one Docker Compose
  setup with no external account required; monitoring is an optional profile on top.

## Quick start

```bash
git clone https://github.com/BulatRuslanovich/music-streaming.git
cd music-streaming
cp .env.example .env
$EDITOR .env
docker compose up -d
```

Set these values before the first start:

```env
POSTGRES_PASSWORD=   # openssl rand -base64 48
JWT_SIGNING_KEY=     # openssl rand -base64 48
OWNER_PASSWORD=      # password for the first admin account
PUBLIC_DOMAIN=       # domain for the automatic HTTPS certificate
```

All optional settings and their defaults are documented in [.env.example](.env.example). The
backend and frontend images are built from the checkout; after pulling new code, rebuild them with
`docker compose up -d --build`.

## Development

```bash
make install     # install frontend dependencies
make dev         # PostgreSQL + API + frontend
make test        # backend and frontend tests (the backend suite needs Docker)
make check       # everything CI runs: formatting, lint, tests
```

The API runs on `http://localhost:5199`, the frontend on `http://localhost:3000`, and the API
browses itself at [`http://localhost:5199/docs`](http://localhost:5199/docs).


## License

MIT — see [LICENSE](LICENSE).
