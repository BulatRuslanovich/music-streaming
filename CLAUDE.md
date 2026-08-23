# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Caimack — a self-hosted music streaming service. .NET 10 API (`backend/`) + Next.js 16 App Router
frontend (`frontend/`), PostgreSQL, files on disk, everything shipped as one Docker Compose stack.

## Commands

```bash
make dev                 # postgres (docker) + `dotnet watch run` + `next dev` together
make db / make db-down   # just postgres, published on 127.0.0.1:5432
make install             # npm install for the frontend
make test                # backend tests (Release); needs docker — the suite starts its own postgres
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
dotnet test MusicStreaming.slnx
dotnet test tests/MusicStreaming.UnitTests --filter "FullyQualifiedName~DiversifierTests"
dotnet format whitespace MusicStreaming.slnx --verify-no-changes   # CI runs this
dotnet format style MusicStreaming.slnx --verify-no-changes        # and this
```

The solution file is `MusicStreaming.slnx` (XML solution format), not a `.sln`.

Migrations (EF Core, migrations live in the Infrastructure assembly, startup project is the API):

```bash
cd backend/src/MusicStreaming.Api
dotnet ef migrations add Name --project ../MusicStreaming.Infrastructure
```

Never hand-edit `*.Designer.cs` or `ApplicationDbContextModelSnapshot.cs`; the license-header script
deliberately skips them. Migrations are applied automatically at startup by `DatabaseInitializer`,
which also seeds the owner account from `Owner:*` configuration — there is no separate migrate step.

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
  configurations + migrations, `FileSystemMusicStorage`, ffmpeg wrappers, TagLib metadata reading,
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

Auth is JWT delivered in HttpOnly cookies (`ms_access`, `ms_refresh`; the refresh cookie is scoped to
`/api/auth`), read back by a `JwtBearerEvents.OnMessageReceived` hook. The fallback authorization
policy requires an authenticated user, so **endpoints are protected by default** — anonymous ones need
explicit `[AllowAnonymous]`, admin ones `[Authorize(Policy = "Admin")]`.

Postgres naming is snake_case via `EFCore.NamingConventions`; entity/property names stay PascalCase.

### Playback and audio

Original files are stored by content hash under `storage/music/<xx>/<yy>/<id><ext>`; derived data
lives in sibling `covers/`, `artists/`, `playlists/`, `transcodes/`, `hls/` directories, all behind
`IMusicStorage` (paths are always resolved back inside the storage root). ffmpeg produces 64/128/192
kbps HLS variants asynchronously: `TranscodeQueue` → `TranscodeWorker`, with
`/api/tracks/{id}/hls/master.m3u8` reporting readiness and `/api/tracks/{id}/stream` falling back to
the original or a cached transcode. `AudioAnalysisQueue` → `AudioAnalysisWorker` extracts audio
features used for similarity. If ffmpeg is missing, `IAudioTranscoder.IsAvailable` is false and the
whole HLS path degrades to the original file rather than failing.

Only one device may play at a time: `/api/playback/session` is an SSE stream backed by
`PlaybackSessionRegistry`, which emits a `displaced` event to the older device.

### Recommendations

Client posts batched playback events to `/api/events` → `EventIngestService` puts them on the
in-memory `EventIngestQueue` (the request returns `202` immediately) → `EventIngestWorker` persists
`PlaybackEvent` rows → `ProfileRollupService` maintains `UserTasteProfile`/`Affinity`/`TrackStats`
with exponential recency decay → `RecommendationWorker` (debounced per user via
`RecommendationRefreshQueue`) runs `CandidateGenerator` → `CandidateScorer` → `Explorer` →
`Diversifier` and writes `RecommendationCacheEntry` rows that the API serves. The scoring pieces in
`Application/Recommendations/Scoring/` are pure and are where the unit tests are.

The whole subsystem is switchable (`Recommendations:Enabled`) and heavily parameterized by
`RecommendationOptions`; integration tests disable it and drive the pipeline steps directly.

### Frontend

App Router, all data through TanStack Query. The shape is deliberate:

- `src/lib/api/*.ts` — one module per API area, merged into a single `api` object in `src/lib/api.ts`.
- `src/lib/queries.ts` — every `queryOptions` (and therefore every query key) in one place; add new
  keys here rather than inlining them in components.
- `src/lib/http.ts` — the fetch wrapper: `ApiError`, cookie credentials, and a single-flight
  `refreshSession()` that retries once on 401 and otherwise fires `onSessionExpired`.
- `src/contexts/*` — cross-page state (`PlayerContext` is the big one; also Auth, Settings, Upload,
  SleepTimer, I18n, Toast).
- Player logic is deliberately extracted from `PlayerContext` into pure, unit-tested modules —
  `playerQueue`, `adaptivePlayback`, `streamRecovery`, `streamCache`, `hlsSessionLoader`,
  `playbackTelemetry`, `djSession`. Put new playback behaviour in one of these, not in the context.

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
  `docker-compose.yml`.
