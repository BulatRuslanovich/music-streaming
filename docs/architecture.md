# Architecture

Caimack turns a personal music collection into a private streaming service. This document is the
map: what the pieces are, how a request travels, and where to start reading. It assumes you have
never seen the repository before.

For settings see [configuration.md](configuration.md), for running it in anger see
[deployment.md](deployment.md), and for the schema see [../db/README.md](../db/README.md).

## The shape of it

```
frontend/  Next.js 16, App Router, React 19, TanStack Query, Tailwind v4
backend/   .NET 10, four projects, one solution (MusicStreaming.slnx)
db/init/   the PostgreSQL schema, as numbered .sql files — there are no EF migrations
storage/   audio originals, HLS renditions, cover art, the CLAP model
deploy/    Caddy, Prometheus, Grafana, Loki — the observability profile
```

The backend is layered, and the layering is enforced by project references only — each arrow is a
`<ProjectReference>`, and there is nothing pointing the other way:

```
Api ──► Infrastructure ──► Application ──► Domain
```

| Project | What lives there | Depends on |
|---|---|---|
| `Domain` | Entities and a handful of pure helpers. 34 files, ~1100 lines | **nothing** — no NuGet packages at all |
| `Application` | Services, DTOs, the recommendation engine, the ports (`Abstractions/`) | EF Core, `Microsoft.Extensions.*` |
| `Infrastructure` | Adapters: PostgreSQL, ffmpeg, ONNX Runtime, Last.fm, the filesystem, background workers | Npgsql, ImageSharp, TagLib, ONNX |
| `Api` | Controllers, middleware, startup, OpenAPI | ASP.NET Core |

Two things are worth knowing up front because they are unusual.

**`Application` depends on EF Core on purpose.** `IApplicationDbContext` is `DbContext` with a
narrower surface, not a repository abstraction: services compose `IQueryable` directly. This keeps
queries readable and close to the code that needs them, at the cost of making the persistence layer
hard to swap. It also means a fair amount of PostgreSQL dialect (`ON CONFLICT`, `::` casts,
`unnest … WITH ORDINALITY`) lives in `Application`.

**There are no EF migrations.** The schema is hand-written SQL in `db/init/`, applied by Postgres on
first start. At boot `SchemaGuard` compares the EF model against `information_schema` and refuses to
start if a table or column the model expects is missing. It checks names only — not types,
nullability, indexes or constraints. [db/README.md](../db/README.md) explains the workflow.

## How a request travels

A page load:

```
browser ──► Next.js (RSC prefetch via lib/server/prefetch.ts)
        └─► /api/* ──► proxy.ts (session gate) ──► ASP.NET controller
                                                      └─► Application service
                                                            └─► IApplicationDbContext ──► Postgres
```

`frontend/src/proxy.ts` is Next 16's middleware. It decides, before any page renders, whether the
visitor looks signed in — the decision itself is a pure function in
`frontend/src/lib/session/sessionGate.ts`, which is unit-tested; the middleware is only plumbing.

Playing a track:

```
<audio> ──► /api/tracks/{id}/stream        progressive, byte ranges
        └─► /api/tracks/{id}/hls/master    adaptive; 202 while ffmpeg is still preparing
```

Only one device may play at a time: `/api/playback/session` is an SSE stream backed by
`PlaybackSessionRegistry`, which sends a `displaced` event to the older device.

## The data model

35 tables. Four groups, and the file numbering in `db/init/` follows them:

| Files | Group | What it holds |
|---|---|---|
| `001`, `010` | extensions, users | `pg_trgm`, the `search_rank` SQL function, accounts, refresh tokens |
| `020` | library | tracks, artists, albums, genres, playlists, favourites |
| `030`, `040` | listening | history, playback events, listening stats, lyrics, settings |
| `050`, `060`, `070` | signals and recommendations | audio features, embeddings, similarity, taste profiles and vectors, cached shelves |
| `080` | integrations | Last.fm accounts, the outbound job queue |

The one to understand first is the difference between four tables that all sound alike:

- **`track_stats`** — global, per track: play count, skip rate, popularity. Not per listener.
- **`user_track_affinities`** — per listener, per track: a decaying score built from playback events.
- **`track_similarity`** — "what is culturally near this track": shared credits, album, genre, year,
  tags, co-occurrence in playlists and sessions. Precomputed, stored pairwise.
- **`track_embeddings`** — a 512-dimension CLAP vector per track: "what does this *sound* like".
  Never compared pairwise in the database; the whole matrix is held in RAM and compared with a dot
  product.

## Recommendations

The largest subsystem — 83 files. It has its own map:
[recommendations.md](recommendations.md).

## Frontend shape

- `src/app/**` — App Router. Each `page.tsx` is a server component that prefetches with the same
  `queryOptions` the client uses, then hydrates.
- `src/lib/api/*.ts` — one module per API area, merged into a single `api` object in `src/lib/api.ts`.
- `src/lib/queries.ts` — every `queryOptions`, and therefore every query key, in one place.
- `src/lib/http.ts` — the fetch wrapper: `ApiError`, cookie credentials, and a single-flight
  `refreshSession()` that retries once on 401.
- `src/lib/playback/**` — the player, in two layers. Pure decision modules (`playerQueue`,
  `adaptivePlayback`, `streamRecovery`, `streamCache`, `djSession`) are unit-tested with no DOM;
  the hooks around them (`usePlaybackEngine`, `useDjSession`, `useMediaSession`) wire them to the
  audio element and to React.
- `src/contexts/*` — cross-page state. `PlayerContext` is the big one and splits into four contexts
  so that a progress tick does not re-render the whole app.
- `src/lib/types/*` — API types, sliced the same way `lib/api` is, re-exported from `lib/types.ts`.

UI text goes through `src/lib/i18n`. `TranslationKey` is derived from `en.ts`, so adding a key there
makes `ru.ts` fail to type-check until it is translated.

## Start here

In this order, the repository explains itself in about an hour:

1. `backend/src/MusicStreaming.Api/Program.cs` — 78 lines, reads top to bottom, names every
   `Startup/*` piece in pipeline order.
2. `backend/src/MusicStreaming.Infrastructure/DependencyInjection.cs` — every port bound to its
   adapter, every background worker, every HTTP client.
3. `backend/src/MusicStreaming.Application/DependencyInjection.cs` — the service catalogue. Note
   `AddCandidateSources`: the registration order there is behaviour, not style.
4. `backend/src/MusicStreaming.Infrastructure/Persistence/ApplicationDbContext.cs` — the whole data
   model as one list of `DbSet`s.
5. `backend/src/MusicStreaming.Application/Options/` — one file per option class; together they are
   the runtime feature list.
6. `backend/src/MusicStreaming.Application/Services/Recommendations/CandidateGenerator.cs` — the hub
   of the biggest subsystem.
7. `frontend/src/lib/api.ts` — the entire client contract in 30 lines.
8. `frontend/src/lib/queries.ts` — every query key.
9. `frontend/src/contexts/PlayerContext.tsx` and `frontend/src/lib/playback/usePlaybackEngine.ts` —
   the two biggest hand-written frontend files; everything player-shaped goes through them.
10. `db/init/` — read in numeric order.

The API browses itself: run the stack and open **`/docs`** for the Scalar UI over the OpenAPI
document at `/openapi/v1.json`. Both are anonymous.

## Conventions worth knowing before you edit

- Every source file carries an SPDX header; CI enforces it.
- **Two languages, split by audience.** English for anything read from outside the repository —
  identifiers, log and exception messages, metric descriptions, OpenAPI text, test names, and the
  `<summary>` of a public type. Russian for prose explaining a non-obvious decision to the next
  person editing the file; when a `<summary>` would carry that prose, the contract goes in
  `<summary>` and the reasoning in `<remarks>`.
- C#: file-scoped namespaces, primary constructors, nullable as an error, `Guid.CreateVersion7()`
  for new ids, injected `TimeProvider` rather than `DateTime.UtcNow` — there are zero uses of the
  latter in `backend/src`.
- Test names are sentences: `An_uploaded_file_becomes_a_track_with_the_metadata_from_its_tags`.
- Integration tests share one `RecommendationApiFixture` (a `WebApplicationFactory` over a
  Testcontainers Postgres seeded from `db/init`) and skip rather than fail when Docker is absent.
- A file over ~300 lines, or a class with more than ~15 members, is a reason to split by
  responsibility. Exceptions: EF configurations, and whole algorithms that lose meaning when
  scattered.
