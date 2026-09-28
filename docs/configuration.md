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
| `MAX_UPLOAD_BYTES` | `Storage:MaxUploadBytes` | `209715200` (200 MB) | Largest accepted audio file |
| `MAX_UPLOAD_BODY_BYTES` | — | `268435456` (256 MB) | Caddy's own body limit. Keep it above `MAX_UPLOAD_BYTES` — multipart framing adds overhead |
| — | `Storage:MaxImageUploadBytes` | `8388608` (8 MB) | Largest accepted cover or artist photo |
| `PUID` / `PGID` | — | `1000` | Owner of the files the backend writes |

## Playback and transcoding

ffmpeg produces 64/128/192 kbps HLS variants in the background. It is required: without it the API
refuses to start, because lower bitrates and ALAC playback exist only through HLS. The bitrates (`AudioBitrates` in the
domain) and the 30 seconds that count as a play (`HistoryService.ThresholdSeconds`) are constants,
not settings.

| `.env` | Key | Default | Meaning |
| --- | --- | --- | --- |
| `HLS_SEGMENT_SECONDS` | `Transcode:HlsSegmentSeconds` | `4` | 2–10. Shorter segments switch quality sooner and cost more requests |
| `TRANSCODE_BACKFILL_ENABLED` | `Transcode:BackfillEnabled` | `true` | Builds the missing variants for tracks that predate transcoding |
| `TRANSCODE_BACKFILL_BATCH` | `Transcode:BackfillBatchSize` | `8` | 1–64 |
| `TRANSCODE_BACKFILL_PAUSE_SECONDS` | `Transcode:BackfillPauseSeconds` | `5` | Pause between batches — this is what keeps the backfill off the CPU you are listening on |
| — | `Transcode:FfmpegPath` | `ffmpeg` | |

## Audio embeddings

A 512-dimension vector per track, produced by a CLAP model under ONNX Runtime. It is what "sounds
like" means everywhere in the app: sonic neighbours, the taste vector, exploration, the radio
queue. Without the model the whole path degrades to the branch a brand new library takes — nothing
fails, there is simply no sonic signal.

The model is roughly 280 MB and is **not** in git. Export it with
`backend/scripts/export_clap_audio_onnx.py` into `<storage>/models/clap`, and deliver it to each
deployment target yourself.

| `.env` | Key | Default | Meaning |
| --- | --- | --- | --- |
| `AUDIO_EMBEDDING_ENABLED` | `AudioEmbedding:Enabled` | `true` | Off leaves the index empty and every sonic term absent |
| `AUDIO_EMBEDDING_PROVIDER` | `AudioEmbedding:Provider` | `clap` | `deterministic` swaps in a stand-in that hashes the file path into a vector. It knows nothing about sound; it exists so the recommendation path can be run locally without the model |
| `AUDIO_EMBEDDING_MODEL_PATH` | `AudioEmbedding:ModelPath` | `models/clap/audio.onnx` | Relative to `Storage:RootPath` |
| `AUDIO_EMBEDDING_MEL_FILTERS_PATH` | `AudioEmbedding:MelFiltersPath` | `models/clap/mel_filters_64x513.f32` | Exported beside the model, not transcribed in code |
| `AUDIO_EMBEDDING_MODEL_SHA256` | `AudioEmbedding:ModelSha256` | — | Empty skips the check. Set it: the model is an executable graph, and a mismatch refuses to load |
| — | `AudioEmbedding:ModelId` | `laion/larger_clap_music_and_speech` | With the slicing strategy this is the algorithm version: changing either re-embeds the whole library, which is hours to a day of CPU |
| `AUDIO_EMBEDDING_INTRA_OP_THREADS` | `AudioEmbedding:IntraOpThreads` | `0` | Threads inside ONNX Runtime; `0` means a quarter of the cores, so streaming does not starve |

Roughly 1.5–2.5 s per track on CPU, so a large library takes hours to a day. The backfill is
ordered by popularity, which puts the transition period on the tail of the library rather than its
head.

## Recommendations

The subsystem is switchable as a whole, and the switch is its only setting. Ranking weights,
penalties and thresholds are constants in `RecommendationTuning` (`Application/Recommendations/Tuning/`):
they are tuned with `make eval`, which measures recall against a popularity baseline, and changing
one is a code change followed by an eval run, not a line in `.env`.

| `.env` | Key | Default | Meaning |
| --- | --- | --- | --- |
| `RECOMMENDATIONS_ENABLED` | `Recommendations:Enabled` | `true` | Off means no mixes, radio or discovery shelves |

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

All optional. Without them the library simply carries less metadata.

| `.env` | Key | Default | Meaning |
| --- | --- | --- | --- |
| `LIBRARY_ENRICHMENT_ENABLED` | `LibraryEnrichment:Enabled` | `true` | Background artist photos and lyrics for newly added tracks |
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
