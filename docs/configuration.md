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

## Required before the first start

| `.env` | Key | Notes |
| --- | --- | --- |
| `POSTGRES_PASSWORD` | — | Also handed to the `postgres` container. `openssl rand -base64 48` |
| `JWT_SIGNING_KEY` | `Jwt:SigningKey` | At least 32 bytes, or the API refuses to start. `openssl rand -base64 48` |
| `OWNER_PASSWORD` | `Owner:Password` | Password for the first admin account, minimum 8 characters |
| `GRAFANA_PASSWORD` | — | Login for the monitoring dashboard on `127.0.0.1:3001` |
| `PUBLIC_DOMAIN` | — | Hostname Caddy issues its certificate for |

## The first account

Seeded by `DatabaseInitializer` on every start, before anything else runs.

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
| — | `Playback:HistoryRetentionEntries` | `1000` | History rows kept per user |
| `TRANSCODE_ENABLED` | `Transcode:Enabled` | `true` | Turning it off leaves only the original files |
| `TRANSCODE_LOW_KBPS` | `Transcode:LowBitrateKbps` | `64` | 32–320, and must not exceed the normal rate |
| `TRANSCODE_NORMAL_KBPS` | `Transcode:NormalBitrateKbps` | `128` | |
| `TRANSCODE_HIGH_KBPS` | `Transcode:HighBitrateKbps` | `192` | |
| `HLS_SEGMENT_SECONDS` | `Transcode:HlsSegmentSeconds` | `4` | 2–10. Shorter segments switch quality sooner and cost more requests |
| `TRANSCODE_BACKFILL_ENABLED` | `Transcode:BackfillEnabled` | `true` | Builds the missing variants for tracks that predate transcoding |
| `TRANSCODE_BACKFILL_BATCH` | `Transcode:BackfillBatchSize` | `8` | 1–64 |
| `TRANSCODE_BACKFILL_PAUSE_SECONDS` | `Transcode:BackfillPauseSeconds` | `5` | Pause between batches — this is what keeps the backfill off the CPU you are listening on |
| — | `Transcode:FfmpegPath` | `ffmpeg` | |
| — | `AudioAnalysis:*` | see below | Audio features behind "similar tracks" |

`AudioAnalysis:Enabled` (`true`), `SampleRateHz` (`8000`), `MaximumSeconds` (`600`),
`BackfillBatchSize` (`4`), `PollSeconds` (`30`).

## Recommendations

The subsystem is switchable as a whole and heavily parameterized; only the settings exposed through
`.env` are listed. The rest live in `RecommendationOptions` and can be set with `Recommendations__*`.

| `.env` | Key | Default | Meaning |
| --- | --- | --- | --- |
| `RECOMMENDATIONS_ENABLED` | `Recommendations:Enabled` | `true` | Off means no mixes, radio or discovery shelves |
| `RECOMMENDATIONS_SHELF_SIZE` | `Recommendations:ShelfSize` | `12` | Items per shelf |
| `RECOMMENDATIONS_EXPLORATION_RATIO` | `Recommendations:ExplorationRatio` | `0.25` | 0–1: share of a shelf given to tracks you have not heard |
| `RECOMMENDATIONS_EVENT_RETENTION_DAYS` | `Recommendations:EventRetentionDays` | `180` | How long raw playback events are kept |

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
| `LASTFM_API_KEY` / `LASTFM_API_SECRET` | `Lastfm:ApiKey` / `Lastfm:ApiSecret` | empty | Enables scrobbling; users connect their own account in settings |
| `LIBRARY_ENRICHMENT_ENABLED` | `LibraryEnrichment:Enabled` | `true` | Background artist photos and lyrics for newly added tracks |
| `AUDIODB_API_KEY` | `AudioDb:ApiKey` | `2` | TheAudioDB, source of artist photos. `2` is their public test key |
| `AUDIODB_REQUEST_DELAY_MS` | `AudioDb:RequestDelayMs` | `1000` | Politeness delay |
| `LRCLIB_REQUEST_DELAY_MS` | `Lrclib:RequestDelayMs` | `500` | Politeness delay for LRCLIB, source of lyrics |
| — | `Lrclib:DurationToleranceSeconds` | `2` | How far a track's length may differ from the matched lyrics |

## Proxy, images and monitoring

| `.env` | Default | Meaning |
| --- | --- | --- |
| `IMAGE_PREFIX` | `ghcr.io/bulatruslanovich/music-streaming` | Registry the images come from |
| `IMAGE_TAG` | `latest` | Pin a version here; `scripts/deploy.sh X.Y.Z` writes it for you |
| `HTTP_PORT` / `HTTPS_PORT` | `80` / `443` | Ports Caddy publishes |
| `BACKEND_PORT` | `8080` | API on `127.0.0.1` only, for debugging |
| `GRAFANA_PORT` | `3001` | Grafana on `127.0.0.1` only |
| `GRAFANA_USER` | `admin` | |
| `PROMETHEUS_RETENTION` | `30d` | |
| `LOKI_RETENTION` | `720h` | |

## Adding a setting

A new setting is four edits, and the build enforces the first three:

1. a property on an options class in `Application/Options/`;
2. a `.Validate(...)` rule on its `AddOptions<T>()` registration in `Infrastructure/DependencyInjection.cs`, ending in `.ValidateOnStart()`;
3. an entry in `.env.example`;
4. the `SCREAMING_CASE → Section__Key` mapping in `docker-compose.yml`.
