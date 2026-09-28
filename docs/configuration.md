# Configuration

Every setting is a bound options object validated at startup: a bad value stops the container with
a message naming the key, rather than misbehaving later. The API reads configuration from
`appsettings.json` (defaults), environment variables (`Section__Key`), and — in development —
`appsettings.Development.json` plus `dotnet user-secrets`.

In Docker the environment variables come from `.env`, which `docker-compose.yml` maps onto the
`Section__Key` names. The tables below give both: the `.env` variable you set, and the configuration
key it lands on.

Anything not listed in [.env.example](../.env.example) has no `.env` variable of its own. To change
one of those, add the `Section__Key` form straight to the `backend` service's `environment:` block.

`appsettings.json` holds only values that differ from the option class's own default. Restating a
default there looks harmless but makes the JSON win at runtime: a default retuned in code would
then never reach the running app. The defaults are the C# ones, and
the tables below quote them.

## Required before the first start

| `.env` | Key | Notes |
| --- | --- | --- |
| `POSTGRES_PASSWORD` | — | Also handed to the `postgres` container. `openssl rand -base64 48` |
| `JWT_SIGNING_KEY` | `Jwt:SigningKey` | At least 32 bytes, or the API refuses to start. `openssl rand -base64 48` |
| `OWNER_PASSWORD` | `Owner:Password` | Password for the first admin account, minimum 8 characters |
| `PUBLIC_DOMAIN` | — | Hostname Caddy issues its certificate for |

## The first account


| `.env` | Key | Default | Meaning |
| --- | --- | --- | --- |
| `OWNER_USERNAME` | `Owner:Username` | `admin` | Lower-cased on seeding |
| `OWNER_PASSWORD` | `Owner:Password` | — | Required only while no user exists |
| `OWNER_RESET_PASSWORD` | `Owner:ResetPasswordOnStartup` | `false` | Resets the owner password to `OWNER_PASSWORD` on the next start — the way back in after losing it. Set it back to `false` afterwards |

## Sessions

| `.env` | Key | Default | Meaning |
| --- | --- | --- | --- |
| `JWT_ACCESS_TOKEN_MINUTES` | `Jwt:AccessTokenMinutes` | `10` | Lifetime of the access cookie; the client refreshes on its own |
| `JWT_REFRESH_TOKEN_DAYS` | `Jwt:RefreshTokenDays` | `30` | How long a signed-in device stays signed in |
| — | `Jwt:Issuer` / `Jwt:Audience` | `music-streaming` | Only worth changing if something else validates the tokens |

Both tokens are HttpOnly cookies (`ms_access`, `ms_refresh`); the refresh cookie is scoped to
`/api/auth`. A refresh token that is presented twice revokes every session of that user.

## Storage

| `.env` | Key | Default | Meaning |
| --- | --- | --- | --- |
| `MUSIC_STORAGE_PATH` | — | `./storage` | Host path mounted at `/storage` |
| — | `Storage:RootPath` | `/storage` | Where the container looks. Originals live in `music/`, derived data in `covers/`, `artists/`, `playlists/`, `hls/` |
| `MAX_UPLOAD_BODY_BYTES` | — | `268435456` (256 MB) | Caddy's own body limit. Keep it above the API's 200 MB — multipart framing adds overhead |
| `PUID` / `PGID` | — | `1000` | Owner of the files the backend writes |

`Storage:RootPath` is the only storage setting because it genuinely differs: `/storage` in the
container, the repository's `storage/` in development, a temporary directory in tests. The upload
limits — 200 MB per audio file, 8 MB per image — are constants (`UploadLimits` in the application
layer).

## Playback and transcoding

ffmpeg produces 64/128/192 kbps HLS variants in the background. It is required: without it the API
refuses to start, because lower bitrates and ALAC playback exist only through HLS. ffmpeg is
looked up on PATH.

Nothing here is a setting. The bitrates (`AudioBitrates` in the domain), the 4-second HLS segment
(`FfmpegAudioTranscoder`), one transcode job per two cores (`TranscodeWorker`), the backfill of
tracks that predate transcoding — 8 variants at a time with a 5-second pause, which is what keeps
it off the CPU you are listening on (`TranscodeBackfillService`) — and the 30 seconds that count as
a play (`HistoryService.ThresholdSeconds`) are all constants.

## Audio embeddings

A 512-dimension vector per track, produced by a CLAP model under ONNX Runtime. It is what "sounds
like" means everywhere in the app: sonic neighbours, the taste vector, exploration, the radio
queue. The model is required: the API does not start without it.

The model is roughly 280 MB and is **not** in git. The one-shot `clap-model` service in
`docker-compose.yml` exports it into `<storage>/models/clap` before the backend starts: the first
run downloads torch and the checkpoint from Hugging Face (about a gigabyte) and takes minutes,
every later run sees `model.json` and exits at once. In development `make model` (part of
`make dev`) runs the same service. To re-export, delete `<storage>/models/clap`.

Nothing here is a setting either: the file paths, the checkpoint id and a quarter of the cores for
ONNX Runtime, so streaming does not starve, are constants in `ClapAudioEmbedder`. The checkpoint id
together with the slicing strategy is the algorithm version — changing either re-embeds the whole
library, which is hours to a day of CPU.

Roughly 1.5–2.5 s per track on CPU, so a large library takes hours to a day. The backfill is
ordered by popularity, which puts the transition period on the tail of the library rather than its
head.

## Recommendations

The subsystem has no settings and cannot be switched off. Ranking weights, penalties and
thresholds are constants in `RecommendationTuning` (`Application/Recommendations/Tuning/`): they are
tuned with `make eval`, which measures recall against a popularity baseline, and changing one is a
code change followed by an eval run, not a line in `.env`.

## Security

Rate limits and the account lockout are constants (`SecurityLimits` in the application layer), not
settings: 10 sign-in attempts per address per minute, 60 uploads, 120 searches and 120 playback
event batches per user per minute, and an account locks for 15 minutes after 10 failed sign-ins.

The per-address limit does nothing against one password guessed from a pool of addresses, which is
what the account lock is for. Counters are in memory, so a restart clears them.

`ForwardedHeaders:KnownNetworks` decides which proxies may set `X-Forwarded-For`; it defaults to the
loopback and private ranges, which covers the bundled Caddy. Widen it only if your proxy sits
elsewhere — the rate limiter partitions on the address it yields.

## External services

Artist photos and lyrics for newly added tracks are fetched in the background, always. When a
service is unreachable the library simply carries less metadata.

| `.env` | Key | Default | Meaning |
| --- | --- | --- | --- |
| `AUDIODB_API_KEY` | `AudioDb:ApiKey` | `2` | TheAudioDB, source of artist photos. `2` is their public test key |
| `AUDIODB_REQUEST_DELAY_MS` | `AudioDb:RequestDelayMs` | `1000` | Politeness delay |
| `LRCLIB_REQUEST_DELAY_MS` | `Lrclib:RequestDelayMs` | `500` | Politeness delay for LRCLIB, source of lyrics |

## Proxy and images

| `.env` | Default | Meaning |
| --- | --- | --- |
| `IMAGE_PREFIX` | `ghcr.io/bulatruslanovich/music-streaming` | Registry the images come from |
| `IMAGE_TAG` | `latest` | Pin a version here; `scripts/deploy.sh X.Y.Z` writes it for you |
| `HTTP_PORT` / `HTTPS_PORT` | `80` / `443` | Ports Caddy publishes |
| `BACKEND_PORT` | `8080` | API on `127.0.0.1` only, for debugging |

## Adding a setting

First, decide whether it is a setting at all. If only the algorithm cares about the value — a
weight, a batch size, a window, a sampling rate — it is a constant next to the code that reads it,
not a setting: a knob nobody turns still has to be documented, validated and carried forever, and a
knob that silently does nothing is worse than no knob at all. Settings are for what differs between
installations: paths, keys, secrets, limits, switches and the size of the machine.

A real new setting is four edits, and the build enforces the first three:

1. a property on an options class in `Application/Options/`;
2. a `.Validate(...)` rule on its `AddOptions<T>()` registration in `Infrastructure/DependencyInjection.cs`, ending in `.ValidateOnStart()`;
3. an entry in `.env.example`;
4. the `SCREAMING_CASE → Section__Key` mapping in `docker-compose.yml`.
