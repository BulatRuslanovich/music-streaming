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
default there looks harmless but makes the JSON win at runtime: a number retuned in
`RecommendationOptions` would then never reach the running app. The defaults are the C# ones, and
the tables below quote them.

## Required before the first start

| `.env` | Key | Notes |
| --- | --- | --- |
| `POSTGRES_PASSWORD` | — | Also handed to the `postgres` container. `openssl rand -base64 48` |
| `JWT_SIGNING_KEY` | `Jwt:SigningKey` | At least 32 bytes, or the API refuses to start. `openssl rand -base64 48` |
| `OWNER_PASSWORD` | `Owner:Password` | Password for the first admin account, minimum 8 characters |
| `PUBLIC_DOMAIN` | — | Hostname Caddy issues its certificate for |

`GRAFANA_PASSWORD` joins them only when the `observability` profile is on — Grafana refuses to
start without it.

## The first account


| `.env` | Key | Default | Meaning |
| --- | --- | --- | --- |
| `OWNER_USERNAME` | `Owner:Username` | `admin` | Lower-cased on seeding |
| `OWNER_DISPLAY_NAME` | `Owner:DisplayName` | the username | Name shown in the interface |
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
| — | `Storage:RootPath` | `/storage` | Where the container looks. Originals live in `music/`, derived data in `covers/`, `artists/`, `playlists/`, `transcodes/`, `hls/` |
| `MAX_UPLOAD_BYTES` | `Storage:MaxUploadBytes` | `209715200` (200 MB) | Largest accepted audio file |
| `MAX_UPLOAD_BODY_BYTES` | — | `268435456` (256 MB) | Caddy's own body limit. Keep it above `MAX_UPLOAD_BYTES` — multipart framing adds overhead |
| — | `Storage:MaxImageUploadBytes` | `8388608` (8 MB) | Largest accepted cover or artist photo |
| `PUID` / `PGID` | — | `1000` | Owner of the files the backend writes |

## Server-side import

Audio copied into `<storage>/import` is added to the library without going through a browser — the
way to bring in a collection that is already on the server. Rejected files move to `import/.failed`
next to a `.txt` naming the reason, so one broken file never blocks later scans.

| `.env` | Key | Default | Meaning |
| --- | --- | --- | --- |
| `LIBRARY_IMPORT_ENABLED` | `LibraryImport:Enabled` | `true` | When off, the folder is neither created nor read |
| `LIBRARY_IMPORT_DIR` | `LibraryImport:Directory` | `import` | Relative to `Storage:RootPath`; must stay inside it |
| `LIBRARY_IMPORT_SCAN_INTERVAL_SECONDS` | `LibraryImport:ScanIntervalSeconds` | `300` | Between automatic scans. Admins can also scan on demand from the upload page |
| `LIBRARY_IMPORT_BATCH` | `LibraryImport:BatchSize` | `50` | Files per scan, so a huge drop is spread over several passes |
| `LIBRARY_IMPORT_MIN_AGE_SECONDS` | `LibraryImport:MinimumAgeSeconds` | `15` | Files written more recently are left alone — they may still be copying |
| `LIBRARY_IMPORT_AFTER` | `LibraryImport:AfterImport` | `delete` | `delete` removes the source once the track is in the library (a copy already lives in `storage/music`); `move` archives it under `import/.imported` instead, which doubles the space that music takes |
| — | `LibraryImport:StartupDelaySeconds` | `20` | Quiet period before the first scan after a restart |

## Playback and transcoding

ffmpeg produces 64/128/192 kbps HLS variants in the background. If ffmpeg is missing the whole HLS
path degrades to serving the original file instead of failing.

| `.env` | Key | Default | Meaning |
| --- | --- | --- | --- |
| `HISTORY_THRESHOLD_SECONDS` | `Playback:HistoryThresholdSeconds` | `30` | Seconds of a track that count as a play |
| `TRANSCODE_ENABLED` | `Transcode:Enabled` | `true` | Turning it off leaves only the original files |
| `TRANSCODE_LOW_KBPS` | `Transcode:LowBitrateKbps` | `64` | 32–320, and must not exceed the normal rate |
| `TRANSCODE_NORMAL_KBPS` | `Transcode:NormalBitrateKbps` | `128` | |
| `TRANSCODE_HIGH_KBPS` | `Transcode:HighBitrateKbps` | `192` | |
| `HLS_SEGMENT_SECONDS` | `Transcode:HlsSegmentSeconds` | `4` | 2–10. Shorter segments switch quality sooner and cost more requests |
| `TRANSCODE_BACKFILL_ENABLED` | `Transcode:BackfillEnabled` | `true` | Builds the missing variants for tracks that predate transcoding |
| `TRANSCODE_BACKFILL_BATCH` | `Transcode:BackfillBatchSize` | `8` | 1–64 |
| `TRANSCODE_BACKFILL_PAUSE_SECONDS` | `Transcode:BackfillPauseSeconds` | `5` | Pause between batches — this is what keeps the backfill off the CPU you are listening on |
| — | `Transcode:FfmpegPath` | `ffmpeg` | |
| — | `AudioAnalysis:Enabled` | `true` | Tempo, key, loudness and energy — the figures on the back of the cover |

`AudioAnalysis` has no other settings on purpose: the sample rate, the analysis window and the
pacing of the backfill are part of the algorithm, and changing one invalidates every feature row
already computed — the same way bumping `AudioAnalysisWorker.AlgorithmVersion` does. They live as
constants next to the code that reads them.

These figures no longer decide which tracks are similar. That question is answered by the CLAP
embedding below; `Energy` is the only one still read by ranking, and only to match a track against
the part of the day.

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
| `AUDIO_EMBEDDING_WORKERS` | `AudioEmbedding:Workers` | `1` | Tracks embedded at once. Above one competes with transcoding |
| `AUDIO_EMBEDDING_INTRA_OP_THREADS` | `AudioEmbedding:IntraOpThreads` | `0` | Threads inside ONNX Runtime; `0` means a quarter of the cores, so streaming does not starve |

Roughly 1.5–2.5 s per track on CPU, so a large library takes hours to a day. The backfill is
ordered by popularity, which puts the transition period on the tail of the library rather than its
head.

## Recommendations

The subsystem is switchable as a whole. Everything below the switch is ranking weights: they are
tuned with `make eval`, which measures recall against a popularity baseline, and they are not part
of the deployment surface. They still live in `RecommendationOptions` and can be overridden with
`Recommendations__*` if you are experimenting, but no `.env` entry advertises them.

| `.env` | Key | Default | Meaning |
| --- | --- | --- | --- |
| `RECOMMENDATIONS_ENABLED` | `Recommendations:Enabled` | `true` | Off means no mixes, radio or discovery shelves |
| — | `Recommendations:EventRetentionDays` | `180` | How long raw playback events are kept |
| — | `Recommendations:TrackSuppressionDays` | `180` | How long a track marked "not interested" stays out. `0` means forever; a blocked artist is always forever |

## Security

| `.env` | Key | Default | Meaning |
| --- | --- | --- | --- |
| `LOGIN_ATTEMPTS_PER_MINUTE` | `Security:LoginAttemptsPerMinute` | `10` | Sign-in attempts per address per minute |
| `UPLOADS_PER_MINUTE` | `Security:UploadsPerMinute` | `60` | Upload requests per user per minute |
| `SEARCHES_PER_MINUTE` | `Security:SearchesPerMinute` | `120` | Search requests per user per minute |
| `EVENTS_PER_MINUTE` | `Security:EventsPerMinute` | `120` | Playback event batches per user per minute |
| `ACCOUNT_LOCKOUT_ATTEMPTS` | `Security:AccountLockoutAttempts` | `10` | Failed sign-ins before the account itself is locked. `0` turns the lock off |
| `ACCOUNT_LOCKOUT_MINUTES` | `Security:AccountLockoutMinutes` | `15` | How long the lock lasts, and the window the failures are counted over |

The per-address limit does nothing against one password guessed from a pool of addresses, which is
what the account lock is for. Counters are in memory, so a restart clears them.

`ForwardedHeaders:KnownNetworks` decides which proxies may set `X-Forwarded-For`; it defaults to the
loopback and private ranges, which covers the bundled Caddy. Widen it only if your proxy sits
elsewhere — the rate limiter partitions on the address it yields.

## External services

All optional. Without them the library simply carries less metadata.

| `.env` | Key | Default | Meaning |
| --- | --- | --- | --- |
| `LASTFM_API_KEY` / `LASTFM_API_SECRET` | `Lastfm:ApiKey` / `Lastfm:ApiSecret` | empty | Enables scrobbling (users connect their own account in settings) and the tag lookups below |
| `LIBRARY_ENRICHMENT_ENABLED` | `LibraryEnrichment:Enabled` | `true` | Background artist photos and lyrics for newly added tracks |
| `TAG_ENRICHMENT_ENABLED` | `TagEnrichment:Enabled` | `true` | Last.fm artist and track tags, the content signal recommendations lean on. Idle without `LASTFM_API_KEY` |
| `TAG_ENRICHMENT_REQUEST_DELAY_MS` | `TagEnrichment:RequestDelayMs` | `350` | Politeness delay between tag lookups |

How many tags are kept per entity, and how long they stay fresh, are not settings: the count shapes
the similarity vector, so it is one decision for the whole system (`TagWeights` in the domain).
| `AUDIODB_API_KEY` | `AudioDb:ApiKey` | `2` | TheAudioDB, source of artist photos. `2` is their public test key |
| `AUDIODB_REQUEST_DELAY_MS` | `AudioDb:RequestDelayMs` | `1000` | Politeness delay |
| `LRCLIB_REQUEST_DELAY_MS` | `Lrclib:RequestDelayMs` | `500` | Politeness delay for LRCLIB, source of lyrics |

## Proxy, images and monitoring

| `.env` | Default | Meaning |
| --- | --- | --- |
| `IMAGE_PREFIX` | `ghcr.io/bulatruslanovich/music-streaming` | Registry the images come from |
| `IMAGE_TAG` | `latest` | Pin a version here; `scripts/deploy.sh X.Y.Z` writes it for you |
| `HTTP_PORT` / `HTTPS_PORT` | `80` / `443` | Ports Caddy publishes |
| `BACKEND_PORT` | `8080` | API on `127.0.0.1` only, for debugging |
| `COMPOSE_PROFILES` | — | Set to `observability` to start the monitoring stack with every `docker compose` command |

The rest applies only to the `observability` profile.

| `.env` | Default | Meaning |
| --- | --- | --- |
| `GRAFANA_PASSWORD` | — | Required; Grafana exits with a message if it is empty |
| `GRAFANA_PORT` | `3001` | Grafana on `127.0.0.1` only |
| `GRAFANA_USER` | `admin` | |
| `PROMETHEUS_RETENTION` | `30d` | |
| `LOKI_RETENTION` | `720h` | |

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
