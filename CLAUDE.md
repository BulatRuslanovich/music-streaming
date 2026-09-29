# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Caimack — a self-hosted music streaming service. .NET 10 API (`backend/`) + Next.js 16 App Router
frontend (`frontend/`), PostgreSQL, files on disk, everything shipped as one Docker Compose stack.

The prose architecture overview lives in [docs/architecture.md](docs/architecture.md) and the
recommendation subsystem in [docs/recommendations.md](docs/recommendations.md). This file is the
operational companion to them: commands, conventions, and the traps that are easy to step in.

## Commands

```bash
make dev                 # postgres + CLAP model (docker) + `dotnet watch run` + `next dev` together
make db / make db-down   # just postgres, published on 127.0.0.1:5432
make db-reset            # drop the dev database and rebuild it from db/init
make model               # export the CLAP model into storage/models/clap if it is not there yet
make install             # npm install for the frontend
make test                # backend + frontend tests; the backend suite needs docker (own postgres)
make test-back / test-front / test-e2e
make eval                # offline recommendation quality: recall@k against a baseline
make fmt                 # dotnet format + prettier + SPDX headers
make fmt-check           # the same checks CI runs
make lint                # eslint over the frontend
make check               # fmt-check + lint + test
make release VERSION=x.y.z   # bump version in both places, commit, tag (does not push)
```

API: `http://localhost:5199`, frontend: `http://localhost:3000`. In dev, `next.config.ts` rewrites
`/api/*` to the backend, so the browser always talks to a same-origin `/api` (see `API_BASE` in
[frontend/src/lib/http.ts](frontend/src/lib/http.ts)). Dev DB credentials are hardcoded in
[appsettings.Development.json](backend/src/MusicStreaming.Api/appsettings.Development.json);
`JWT_SIGNING_KEY` comes from `.env` or `dotnet user-secrets` (`UserSecretsId: music-streaming-api`).

### Backend

```bash
cd backend
dotnet build MusicStreaming.slnx
dotnet test --solution MusicStreaming.slnx
dotnet test --project tests/MusicStreaming.UnitTests --filter-class "*DiversifierTests"
dotnet format whitespace MusicStreaming.slnx --verify-no-changes   # CI runs this
dotnet format style MusicStreaming.slnx --verify-no-changes        # and this
```

The solution file is `MusicStreaming.slnx` (XML solution format), not a `.sln`. Tests run on
Microsoft.Testing.Platform (xunit v3) — the opt-in lives in `global.json` at the repo root, and on
the .NET 10 SDK `dotnet test` takes `--solution`/`--project` instead of a bare path, with xunit's
`--filter-class`/`--filter-method` instead of VSTest's `--filter`.

### Database

There are no EF migrations. The schema is a module of its own in [db/](db/): numbered SQL files in
`db/init`, mounted into the postgres container as `/docker-entrypoint-initdb.d`, so an empty
database builds itself on first start and nothing else ever touches it.

The EF configurations (`Persistence/Configurations`) hold **only mapping** — what conventions plus
snake_case cannot infer: composite keys and keys named `UserId`/`TrackId`, one-to-one sides, a key
that is also the foreign key (without `HasForeignKey` EF invents a shadow `user_id1` column), delete
behaviour that differs from EF's default, jsonb converters, array comparers and the five table
names that are not the snake-cased `DbSet` name. Indexes, lengths and nullability live only in
`db/init`; do not mirror them back into the model.

Changing the schema means editing the entity **and** the matching file in `db/init` (plus a
configuration only if the new mapping is one of the cases above), then applying the same `ALTER` to any live database by hand (`db/README.md`). Locally
the way to pick up an edit is `make db-reset` — postgres runs those scripts only on an empty volume.

`DatabaseInitializer` waits for the database, then seeds the owner account from `Owner:*`; it does
not compare the schema with the EF model. Integration tests build their container from the same
`db/init`, so a schema change forgotten there fails the suite on the first query that needs it.

### Frontend

```bash
cd frontend
npm run dev / build / start
npm run lint          # eslint
npm run format:check  # prettier — CI fails on unformatted files
npm test              # vitest, node environment
npx vitest run src/lib/playerQueue.test.ts
```

Vitest only picks up `src/**/*.test.ts` (not `.tsx`) — the tested logic lives in plain modules under
`src/lib/`, components are not unit-tested.

### Before pushing

`scripts/license-headers.sh --check` — every `.cs/.ts/.tsx/.js/.mjs/.css` file must start with the
two-line SPDX header. Run `scripts/license-headers.sh` (no args) to stamp missing ones. This is its
own CI job, so a new file without the header fails the build.

## Architecture

### Backend layering

`Api → Infrastructure → Application → Domain`. References only point inward; `Api` references
`Infrastructure` (to compose DI), never `Application` alone.

- **Domain** — EF entities plus pure helpers (`Normalize`, `Translit`, `ArtistNames`, `AudioQuality`).
  No packages, no EF references.
- **Application** — the actual work. Services (`CatalogService`, `StreamingService`,
  `RecommendationService`, …) are plain classes taking `IApplicationDbContext` and abstractions from
  `Abstractions/`; DTOs are records in `Dtos/`; option classes with `Validate(...).ValidateOnStart()`
  live in `Options/`. It depends on EF Core (for `IQueryable`) but knows nothing about Npgsql or HTTP.
- **Infrastructure** — the implementations of those abstractions: `ApplicationDbContext` +
  configurations, `FileSystemMusicStorage`, ffmpeg wrappers, TagLib metadata reading,
  ImageSharp, BCrypt, JWT, HTTP clients (TheAudioDB, LRCLIB), and every `BackgroundService`.
- **Api** — thin controllers that delegate to a single service and return `Ok(...)`, plus
  `Startup/*` extension methods that `Program.cs` calls in order.

Two DI entry points: `AddApplication()` (Application) and `AddInfrastructure(configuration)`
(Infrastructure). A new service must be registered in one of them — there is no assembly scanning.

Errors: throw `AppException` subclasses (`NotFoundException`, `ValidationException`,
`ConflictException`, `ForbiddenException`, `UploadTooLargeException`) from services;
`ExceptionHandlingMiddleware` turns them into RFC 7807 `application/problem+json`. Controllers do not
build error responses.

Entity → DTO mapping goes through `Application/Common/ToDto.cs`, which exposes `Expression<Func<,>>`
projections so they compose into EF queries (`.Select(ToDto.Track(userId))`) — do not add
hand-written mapping in services or a mapping library.

Auth is JWT delivered in HttpOnly cookies (`ms_access`, `ms_refresh`, both at path `/` — the frontend
proxy (`frontend/src/proxy.ts`, the Next 16 name for what used to be `middleware.ts`) decides access on
page navigations and has to see the refresh cookie there; narrowing it to
`/api/auth` locked every listener out), read back by a `JwtBearerEvents.OnMessageReceived` hook. The
fallback authorization
policy requires an authenticated user, so **endpoints are protected by default** — anonymous ones need
explicit `[AllowAnonymous]`, admin ones `[Authorize(Policy = "Admin")]`.

Postgres naming is snake_case via `EFCore.NamingConventions`; entity/property names stay PascalCase.

### Playback and audio

Original files are stored by content hash under `storage/music/<xx>/<yy>/<id><ext>`; derived data
lives in sibling `covers/`, `artists/`, `playlists/`, `hls/` directories, all behind
`IMusicStorage` (paths are always resolved back inside the storage root). ffmpeg produces 64/128/192
kbps HLS variants asynchronously: `TranscodeQueue` → `TranscodeWorker`, with
`/api/tracks/{id}/hls/master.m3u8` reporting readiness and `/api/tracks/{id}/stream` always serving
the original — HLS is the only place a lower bitrate exists.

`AudioEmbeddingQueue` → `AudioEmbeddingWorker` is the only audio analysis: it runs the CLAP
audio tower under ONNX Runtime (`ClapAudioEmbedder`) over three 10-second windows and stores one
512-d unit vector per track in `track_embeddings`. Roughly 1.5–2.5 s per track, so a large library
takes hours to a day; the backfill is ordered by popularity so the transition period is felt on the
tail of the library rather than its head. The model is ~280 MB and is **not** in git: the
one-shot `clap-model` compose service (`backend/scripts/Dockerfile.clap`; `make model`, part of
`make dev`) runs `export_clap_audio_onnx.py` into `<storage>/models/clap` on first start and exits
at once when `model.json` is already there — the same way `db/init` builds an empty database. The
model is required, like ffmpeg: `AudioEmbeddingWorker` loads it on start and the host does not come
up without it. Tracks still go without a vector while they wait in the queue or the backfill, so
the "no embedding" branches downstream stay. ONNX Runtime ships glibc-only natives, which is why the runtime image is
bookworm-slim rather than Alpine, and why the package is pinned to 1.23.2 — 1.24.1 does not load on
Linux at all. ffmpeg is required: `TranscodeWorker` checks it on start and the host does not come
up without it. The integration suite removes both transcode workers and the embedding worker, so
it needs neither ffmpeg nor the model.

Only one device may play at a time: `/api/playback/session` is an SSE stream backed by
`PlaybackSessionRegistry`, which emits a `displaced` event to the older device.

### Recommendations

Client posts batched playback events to `/api/playback/signals` (the path deliberately avoids the word
"events", which ad blockers treat as analytics) → `EventIngestService` puts them on the in-memory
`EventIngestQueue` (the request returns `202` immediately) → `EventIngestWorker` persists
`PlaybackEvent` rows → `ProfileRollupService` maintains `UserTasteProfile`/`Affinity` with
exponential recency decay, folds each event into the listener's single **taste vector**
(`user_taste_vectors`, EMA at `RecommendationTuning.Vector.Alpha`) and accumulates the directed
`track_transitions` graph → `RecommendationWorker` (debounced per user via
`RecommendationRefreshQueue`) runs `CandidateGenerator` → `CandidateScorer` → `Explorer` →
`Diversifier` and writes `RecommendationCacheEntry` rows that the API serves. The scoring pieces in
`Application/Recommendations/Scoring/` are pure and are where the unit tests are.

`CandidateGenerator` does not know where candidates come from: each way of naming tracks is an
`ICandidateSource` in `Application/Recommendations/Sources/`, and the generator only loads the
user's context, merges what the sources return and materialises the result. **The registration
order in `AddCandidateSources` is behaviour, not style** — numeric signals merge by maximum, but
the source and the explanation text ("sounds like X") go to whichever source named the track first.

Only what the home page shows is generated: `forYou`, one `becauseYouListened` (the top artist),
`discover` (the fallback second shelf for a listener without a favourite artist) and
`artistsForYou`, plus a hidden `mixPool` that is never served as a shelf. The mix of the day
(`DailyMixSnapshotStore`, hero block and `/api/home/mixes/daily`) is a snapshot, not a query: the
first request of a listener's local day draws 60 tracks out of `mixPool` with
`DailyMix.PickWeighted` and stores them in `daily_mixes` keyed by `(UserId, LocalDate)`; every
later request that day replays that row, because the worker re-runs after each session.

Similarity is sonic only: cosine between CLAP embeddings, held in RAM by `IEmbeddingIndex` and never
stored pairwise. `CandidateGenerator` fills `AudioSimilarity` and `TasteFit` from the index in one
pass. The taste vector puts the listener and the tracks in the same space, so one dot product
answers "does this sound like what they like"; `TasteVectorReader` serves it to shelves and radio,
folding events newer than the rollup watermark in memory. `Explorer` takes its far basket from the
bottom quartile of taste similarity, i.e. tracks that *sound* different.

The radio (`RadioService`, `/api/recommendations/radio`) is the only queue generator: the client
calls it both to continue a queue (autoplay) and for an explicit "radio from this track". It runs
through `QueueBuilder` (pure, `Recommendations/Queue/`), an additive score over the index: taste,
closeness to the playing track, a new-in-library boost that decays with age, and the transition
edge. It enforces its own hard limits — at most two tracks per artist, no duplicate content hash or
artist|title — and interleaves the far basket so exploration never opens the queue.
`FlowQueueService` does the database work around it. While the index is empty the radio returns an
empty batch. There is no server-side session: the client owns the queue.

`LibraryMaintenance` (run by `LibraryMaintenanceWorker`) refreshes `track_stats`, prunes old
events and expired suppressions, decays the transition graph and removes orphaned albums, artists
and genres.

The subsystem has no settings, not even an on/off switch; every weight, penalty and threshold is a
constant in `RecommendationTuning` (`Recommendations/Tuning/`, one file per consumer group).
Integration tests remove its background workers (`RecommendationApiFixture`) and drive the pipeline
steps directly; `fixture.EmbedLibraryAsync()` gives the seeded tracks random vectors and loads the
index when a test needs the sonic path.

`make eval` (`RecommendationQualityTests` + `Evaluation/`) replays a synthetic listening history,
splits it in time, builds shelves from the past only and **prints** recall@k against the held-out
days and against a popularity baseline, the share of the listener's own scene and the artist spread.
It asserts only that the feed is not empty and that tracks without an embedding are not skewed —
the quality numbers are for reading when you change a weight, not a gate: the subsystem was
simplified at their expense on purpose.

### Frontend

App Router, all data through TanStack Query. The shape is deliberate:

- `src/lib/api/*.ts` — one module per API area, merged into a single `api` object in `src/lib/api.ts`.
- `src/lib/queries.ts` — every `queryOptions` (and therefore every query key) in one place; add new
  keys here rather than inlining them in components.
- `src/lib/http.ts` — the fetch wrapper: `ApiError`, cookie credentials, and a single-flight
  `refreshSession()` that retries once on 401 and otherwise fires `onSessionExpired`.
- `src/contexts/*` — cross-page state (`PlayerContext` is the big one; also Auth, Settings, Upload,
  SleepTimer, I18n, Toast).
- Player logic is deliberately extracted from `PlayerContext`, which is left an orchestrator over
  queue state and the public API. Two layers: pure, unit-tested decision modules — `playerQueue`,
  `adaptivePlayback`, `streamRecovery`, `streamCache`, `hlsSessionLoader`, `playbackTelemetry`,
  `radioSession` — and the hooks/classes wiring them to the audio element and React:
  `usePlaybackEngine`, `playbackRecovery` (the stateful driver around `streamRecovery`),
  `useStreamPrefetch`, `useRadioSession`, `usePlayerStorage`, `useMediaSession`, `useExclusivePlayback`.
  Put new playback behaviour in one of these, not in the context; put the part that is a decision
  in the first layer, where the tests are.

UI text goes through `src/lib/i18n` (`en`/`ru` dictionaries, `TranslationKey` is derived from `en`,
so adding a key to `en.ts` makes `ru.ts` fail to type-check until translated). Components use Radix
primitives + Tailwind v4 via `src/components/ui`.

## Conventions

- SPDX header on every source file (enforced in CI, see above).
- Two languages, split by audience, not by file. **English** for everything someone outside this
  repository reads: identifiers, log and exception messages, metric descriptions, OpenAPI text,
  test names, and the `<summary>` of any public type, interface or controller — those are the
  contract. **Russian** for prose explaining a non-obvious decision to whoever edits the file next;
  when a `<summary>` would carry that prose, put the English contract in `<summary>` and the
  Russian reasoning in `<remarks>`.
- C#: file-scoped namespaces, primary constructors for services/controllers/workers, nullable enabled
  with `WarningsAsErrors=nullable`, `Guid.CreateVersion7()` for new ids, `TimeProvider` (injected)
  instead of `DateTime.UtcNow` where time matters.
- Integration tests share one `RecommendationApiFixture` (`WebApplicationFactory` + a Testcontainers
  postgres) through `[Collection(nameof(RecommendationApiCollection))]`, seed via `LibrarySeeder`, and
  start with `Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason)` so the suite skips
  rather than fails without Docker. Test names are sentences:
  `An_uploaded_file_becomes_a_track_with_the_metadata_from_its_tags`.
- The version lives in `backend/Directory.Build.props` and `frontend/package.json` and must stay in
  sync. Only `scripts/release.sh` changes it.
- A number nobody changes per installation is a constant next to its consumer (`SecurityLimits`,
  `UploadLimits`, `RecommendationTuning`, the private constants in the workers), not a setting — and
  there are no on/off switches for subsystems. Settings are only what really differs between
  installations: secrets, the owner account, `Storage:RootPath`, external API keys.
- Configuration is bound options with `.ValidateOnStart()`; a new setting means an option property, a
  validation rule, an `.env.example` entry, and the `SCREAMING_CASE → Section__Key` mapping in
  `docker-compose.yml`. The rule lives next to the property it guards, in the option class's static
  `Validated(...)` method; `AddInfrastructure` only binds the section.
- A file in `src/` over ~300 lines, or a class with more than ~15 members, is a reason to split by
  responsibility rather than a sign of a hard problem. The exception is whole algorithms that lose meaning when scattered (DSP, SQL pipelines). This
  is a review norm, not a CI rule.
