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