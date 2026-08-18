<h1 align="center">Caimack</h1>


<p align="center"> <strong>Personal music streaming service for your own library.</strong> </p>

<p align="center"> Self-hosted · Private · Simple </p>


<p align="center"> <img src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white"> <img src="https://img.shields.io/badge/Next.js-22-000000?logo=next.js&logoColor=white"> <img src="https://img.shields.io/badge/PostgreSQL-17-4169E1?logo=postgresql&logoColor=white"> <img src="https://img.shields.io/badge/Docker-Compose-2496ED?logo=docker&logoColor=white"> </p>

## Quick start

```bash
git clone <this repo>
cd music-streaming

cp .env.example .env
$EDITOR .env

docker compose up -d
```

Required variables:

```env
POSTGRES_PASSWORD=
JWT_SIGNING_KEY=
OWNER_PASSWORD=
PUBLIC_DOMAIN=
```

Images come prebuilt from GHCR, so nothing is compiled on the server. To build them yourself:

```bash
docker compose -f docker-compose.yml -f docker-compose.build.yml up -d --build
```

## Releasing

```bash
scripts/release.sh 1.3.0                       # bumps both versions, tags v1.3.0
git push origin master && git push origin v1.3.0
```

Pushing the tag starts the `Release` workflow, which builds `backend`, `frontend` and
`artist-images` and pushes them to `ghcr.io/bulatruslanovich/music-streaming/*`, tagged with the
version and `latest`.

On the server:

```bash
scripts/deploy.sh 1.3.0
```

GHCR packages are private until you say otherwise. Either make the three packages public in the
repository's *Packages* settings, or log the server in once with a token that has `read:packages`:

```bash
echo "$GHCR_TOKEN" | docker login ghcr.io -u bulatruslanovich --password-stdin
```

It pins `IMAGE_TAG` in `.env`, pulls, and recreates only the containers whose image changed —
PostgreSQL, Caddy and the monitoring stack stay up. Rolling back is the same command with the
previous version.



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



<p align="center">
  <sub>Built for personal music libraries.</sub>
</p>
