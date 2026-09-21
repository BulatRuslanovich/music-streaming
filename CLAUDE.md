# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Caimack — a self-hosted music streaming service. .NET 10 API (`backend/`) + Next.js 16 App Router
frontend (`frontend/`), PostgreSQL, files on disk, everything shipped as one Docker Compose stack.

## Commands

```bash
make dev                 # postgres (docker) + `dotnet watch run` + `next dev` together
make db / make db-down   # just postgres, published on 127.0.0.1:5432
make db-reset            # drop the dev database and rebuild it from db/init
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
database builds itself on first start and nothing else ever touches it. The grouping of the files
mirrors the EF configurations, one file per `IEntityTypeConfiguration`.

Changing the schema means editing the entity, its configuration **and** the matching file in
`db/init`, then applying the same `ALTER` to any live database by hand (`db/README.md`). Locally
the way to pick up an edit is `make db-reset` — postgres runs those scripts only on an empty volume.

`DatabaseInitializer` waits for the database, has `SchemaGuard` compare the EF model against
`information_schema` and refuses to start if anything is missing, naming it; then it seeds the owner
account from `Owner:*`. Integration tests build their container from the same `db/init`, so a
schema change forgotten there fails the suite.

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
  ImageSharp, BCrypt, JWT, HTTP clients (Last.fm, TheAudioDB, LRCLIB), and every `BackgroundService`.
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
lives in sibling `covers/`, `artists/`, `playlists/`, `transcodes/`, `hls/` directories, all behind
`IMusicStorage` (paths are always resolved back inside the storage root). ffmpeg produces 64/128/192
kbps HLS variants asynchronously: `TranscodeQueue` → `TranscodeWorker`, with
`/api/tracks/{id}/hls/master.m3u8` reporting readiness and `/api/tracks/{id}/stream` falling back to
the original or a cached transcode. `AudioAnalysisQueue` → `AudioAnalysisWorker` extracts scalar
audio features — tempo, percussive activity, brightness, rolloff, loudness, dynamic range and key.
These feed mood and daypart energy; they are **not** how tracks are compared to each other any
more (see Recommendations). Bumping `AudioAnalysisWorker.AlgorithmVersion` re-extracts the library.

`AudioEmbeddingQueue` → `AudioEmbeddingWorker` is the second, far slower analysis: it runs the CLAP
audio tower under ONNX Runtime (`ClapAudioEmbedder`) over three 10-second windows and stores one
512-d unit vector per track in `track_embeddings`. Roughly 1.5–2.5 s per track, so a large library
takes hours to a day; the backfill is ordered by popularity so the transition period is felt on the
tail of the library rather than its head. The model is ~280 MB and is **not** in git — export it
with `backend/scripts/export_clap_audio_onnx.py` into `<storage>/models/clap`. Without it
`IAudioEmbedder.IsAvailable` is false and everything downstream takes the same branch a brand new
library does. ONNX Runtime ships glibc-only natives, which is why the runtime image is
bookworm-slim rather than Alpine, and why the package is pinned to 1.23.2 — 1.24.1 does not load on
Linux at all. If ffmpeg is missing,
`IAudioTranscoder.IsAvailable` is false and the whole HLS path degrades to the original file rather
than failing.

Only one device may play at a time: `/api/playback/session` is an SSE stream backed by
`PlaybackSessionRegistry`, which emits a `displaced` event to the older device.

### Recommendations

Client posts batched playback events to `/api/playback/signals` (the path deliberately avoids the word
"events", which ad blockers treat as analytics) → `EventIngestService` puts them on the in-memory
`EventIngestQueue` (the request returns `202` immediately) → `EventIngestWorker` persists
`PlaybackEvent` rows → `ProfileRollupService` maintains `UserTasteProfile`/`Affinity`/`TrackStats`
with exponential recency decay, folds each event into the listener's **taste vector**
(`user_taste_vectors`: one global plus one per daypart, EMA at `Recommendations:TasteAlpha`) and
accumulates the directed `track_transitions` graph → `RecommendationWorker` (debounced per user via
`RecommendationRefreshQueue`) runs `CandidateGenerator` → `CandidateScorer` → `Explorer` →
`Diversifier` and writes `RecommendationCacheEntry` rows that the API serves. The scoring pieces in
`Application/Recommendations/Scoring/` are pure and are where the unit tests are.

`CandidateGenerator` does not know where candidates come from: each way of naming tracks is an
`ICandidateSource` in `Application/Recommendations/Sources/`, and the generator only loads the
user's context, merges what the sources return and materialises the result. **The registration
order in `AddCandidateSources` is behaviour, not style** — numeric signals merge by maximum, but
the source and the explanation text ("because you listened to X") go to whichever source named the
track first. Reordering the registrations rewrites the captions on the shelves; `make eval` and
`RecommendationPipelineTests` are what catch it.

`ProfileRollupService` also builds a taste per part of the day (`UserTasteProfile.Dayparts`) from
`ListeningStat`, read in the listener's own time zone. Shelves for all four parts are generated
together and `RecommendationService` serves only the one matching the listener's local clock —
generation runs hours before delivery, so the choice cannot be made at generation time.

The mix of the day (`DailyMixSnapshotStore`, hero block and `/api/home/mixes/daily`) is a snapshot, not a
query: the first request of a listener's local day draws 60 tracks out of the recommendation shelves
with `DailyMix.PickWeighted` and stores them in `daily_mixes` keyed by `(UserId, LocalDate)`; every
later request that day replays that row. The shelves underneath move — the worker re-runs after each
session and dayparts swap the shelves around the clock — so without the snapshot "today's mix" would
be rewritten several times a day.

Similarity has two halves answering different questions. `track_similarity` is the **cultural**
one: shared credits, album, genre, year, duration, Last.fm tags and co-occurrence in sessions and
playlists. The **sonic** one is cosine between CLAP embeddings, held in RAM by `IEmbeddingIndex`
and never stored pairwise. `CandidateGenerator` fills `AudioSimilarity` and `TasteFit` from the
index in one pass, so neither costs a database round trip.

`SimilarityMaintenance` rebuilds `track_similarity` on a schedule, but only for what changed:
`track_similarity_state` stores a fingerprint of every track's inputs (metadata, credits, embedding
cluster, tags, plays, playlist membership), and a pass recomputes the changed tracks plus everything
they pair with. Pair candidates come partly from embedding clusters (`cluster_core` in
`build-pairs.sql`): the DSP buckets that used to do that job were one of seven pair generators, and
dropping them outright could have left sparsely tagged tracks with no neighbours at all. Nothing changed means the pass does nothing; a quarter of the library changed, or a
day has passed, means a full rebuild. Popularity is deliberately outside the fingerprint — it moves
every pass and only decides which tracks represent a genre or a tag, so that drift is what the daily
full rebuild is for.

The taste vector is what makes the sonic half usable: it puts the listener and the tracks in the
same space, so one dot product answers "does this sound like what they like". `TasteVectorReader`
serves it to both shelves and radio, folding events newer than the rollup watermark in memory so an
interactive session is not a minute stale. Exploration follows from it — `Explorer` takes its far
basket from the bottom quartile of taste similarity, i.e. tracks that *sound* different, where it
used to take whatever the listener simply had not heard.

Radio and DJ `Flow`/`Discover` run through `QueueBuilder` (pure, `Recommendations/Queue/`), an
additive score over the index: taste, closeness to the playing track, a new-in-library boost that
decays with age and impressions, and the transition edge. It enforces its own hard limits — at most
two tracks per artist, no duplicate content hash or artist|title — and interleaves the far basket so
exploration never opens the queue. `FlowQueueService` does the database work around it. There is no
server-side session: the client owns the queue, and only the 48-hour exclude seed and the anchor
pick moved server-side.

The whole subsystem is switchable (`Recommendations:Enabled`) and heavily parameterized by
`RecommendationOptions`; integration tests disable it and drive the pipeline steps directly.

Weights are not guesses: `make eval` (`RecommendationQualityTests` + `Evaluation/`) replays a
synthetic listening history, splits it in time, builds shelves from the past only and measures
recall@k against the held-out days and against a popularity baseline. Change a weight, run it, keep
the change only if the numbers move the right way. The evaluation catalogue spreads one taste over
several genres on purpose — equating a taste with a genre measures `MaxPerGenre`, not the ranking.

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
  `djSession` — and the hooks/classes wiring them to the audio element and React:
  `usePlaybackEngine`, `playbackRecovery` (the stateful driver around `streamRecovery`),
  `useStreamPrefetch`, `useDjSession`, `usePlayerStorage`, `useMediaSession`, `useExclusivePlayback`.
  Put new playback behaviour in one of these, not in the context; put the part that is a decision
  in the first layer, where the tests are.

UI text goes through `src/lib/i18n` (`en`/`ru` dictionaries, `TranslationKey` is derived from `en`,
so adding a key to `en.ts` makes `ru.ts` fail to type-check until translated). Components use Radix
primitives + Tailwind v4 via `src/components/ui`.

## Conventions

- SPDX header on every source file (enforced in CI, see above).
- Prose comments explaining a non-obvious decision are written in Russian, mixed with English ones;
  identifiers, log messages, exception text and anything user-facing are English. Match the file.
- C#: file-scoped namespaces, primary constructors for services/controllers/workers, nullable enabled
  with `WarningsAsErrors=nullable`, `Guid.CreateVersion7()` for new ids, `TimeProvider` (injected)
  instead of `DateTime.UtcNow` where time matters.
- Integration tests share one `RecommendationApiFixture` (`WebApplicationFactory` + a Testcontainers
  postgres) through `[Collection(nameof(RecommendationApiCollection))]`, seed via `LibrarySeeder`, and
  start with `Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason)` so the suite skips
  rather than fails without Docker. Test names are sentences:
  `An_uploaded_file_becomes_a_track_with_the_metadata_from_its_tags`.
- The version lives in `backend/Directory.Build.props` and `frontend/package.json` and must stay in
  sync (the footer shows both). Only `scripts/release.sh` changes it.
- Configuration is bound options with `.ValidateOnStart()`; a new setting means an option property, a
  validation rule, an `.env.example` entry, and the `SCREAMING_CASE → Section__Key` mapping in
  `docker-compose.yml`. The rule lives next to the property it guards, in the option class's static
  `Validated(...)` method; `AddInfrastructure` only binds the section.
- A file in `src/` over ~300 lines, or a class with more than ~15 members, is a reason to split by
  responsibility rather than a sign of a hard problem. The exceptions are EF configurations, which
  group by theme, and whole algorithms that lose meaning when scattered (DSP, SQL pipelines). This
  is a review norm, not a CI rule.
