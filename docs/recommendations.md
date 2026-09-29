# Recommendations

The largest subsystem: 66 files across `Application/Recommendations/`,
`Application/Services/Recommendations/` and `Infrastructure/Recommendations/`. This is its map.

## One notion of "similar"

Similarity is sonic only. A CLAP model turns every track into a 512-dimension unit vector; the whole
matrix lives in RAM (`IEmbeddingIndex`) and two tracks are compared with one dot product. Nothing is
stored pairwise.

A listener's taste is a point in the same space (`user_taste_vectors`, one per listener), which is
why "how close is this track to what you like" is also one dot product.

What the listener does is kept separately, as scalar affinities to tracks, artists and genres with
exponential decay (`user_*_affinity`), and as a directed graph of which track followed which
(`track_transitions`).

## The path a play takes

```
client batches events
   └─► POST /api/playback/signals            (the path avoids the word "events": ad blockers eat it)
         └─► EventIngestQueue                 in-memory, the request returns 202 immediately
               └─► EventIngestWorker          persists PlaybackEvent rows
                     └─► ProfileRollupService one pass, one watermark, exactly once:
                           ├─ affinities to tracks, artists and genres, with decay
                           ├─ the taste vector (EMA, RecommendationTuning.Vector.Alpha)
                           └─ the directed track_transitions graph
                                 └─► RecommendationRefreshQueue     debounced per listener
                                       └─► RecommendationWorker
                                             └─► CandidateGenerator
                                                   └─► CandidateScorer
                                                         └─► Explorer
                                                               └─► Diversifier
                                                                     └─► recommendation_cache_entries
```

The API then serves those cached rows.

## What is generated

Only what the home page shows (`HomeBlocks`): `forYou`, one `becauseYouListened` for the top
artist, `discover` as the second shelf for a listener who has no favourite artist yet, and
`artistsForYou`. Next to them sits a hidden `mixPool`: about 120 candidates composed the same way
as a shelf, never served as one. The mix of the day draws 60 tracks from it once per local day
(`DailyMixSnapshotStore`) and replays that snapshot until midnight.

## Where candidates come from

`CandidateGenerator` does not know where candidates come from. Each way of naming tracks is an
`ICandidateSource` in `Application/Recommendations/Sources/`: sonic neighbours of recent plays,
loved artists, loved genres, playlist neighbours, closeness to the taste vector, new and popular in
the library, and not yet heard. The generator loads the listener's context, merges what the sources
return, and materialises the result.

**The registration order in `AddCandidateSources` is behaviour, not style.** Numeric signals merge
by maximum, but the source and the explanation text ("sounds like X") go to whichever source named
the track first.

Sources are grouped into families (`CandidateSourceFamily`), and the multi-source bonus counts
*families*, not sources: loved artists and loved genres lean on the same listening history, so
agreeing with each other proves little.

## The radio

`RadioService` is the only queue generator. The client calls it to continue a queue that is about
to run out (autoplay) and for an explicit "radio from this track". `QueueBuilder` scores the whole
index additively — taste, closeness to the playing track, a new-in-library boost that fades with age,
and the transition edge — and interleaves a far basket of tracks that sound unlike the taste, never
opening the queue with one. `FlowQueueService` supplies the database side: what was heard in the
last 48 hours and which track to anchor on when none was named. While the index is empty the radio
returns an empty batch.

## What is pure and what is not

**Pure — no I/O, no framework, unit-tested:**

| Folder | What |
|---|---|
| `Recommendations/Scoring/` | ranking weights, penalties, MMR diversification, near/far exploration, taste signals, decay |
| `Recommendations/Embeddings/` | the in-RAM matrix, vector maths, spherical k-means, the EMA fold |
| `Recommendations/Queue/` | the radio queue builder |

Their dependency surface is `Domain` entities, the `RecommendationTuning` constants and
`System.Numerics.Tensors`. Nothing there reads configuration.

**Not pure:** the candidate sources, `SuppressionSet`, everything in `Services/Recommendations/`,
and all of `Infrastructure/Recommendations/`.

## Audio embeddings

A CLAP model under ONNX Runtime turns each track into a 512-dimension unit vector. Three 10-second
windows — start, middle, end — are averaged and normalised; the strategy token is `clap_3x10_v1`.

The model is ~280 MB and is **not** in git. The one-shot `clap-model` compose service exports it
into `<storage>/models/clap` on first start (`make model` in development), and the API refuses to
start without it. A track still has no vector while it waits for the worker, so every sonic term
stays optional per track: `RankingWeights.Combine` hands an absent signal's weight to the others,
and `Explorer` never puts a track without a vector in the far basket.

Roughly 1.5–2.5 s per track on CPU, so a large library takes hours to a day. The backfill is ordered
by popularity, so the transition period is felt on the tail of the library rather than its head.

## Background passes

| Worker | Cadence | Does |
|---|---|---|
| `EventIngestWorker` | continuous | drains the event queue into rows |
| `RecommendationWorker` | debounced per listener | regenerates that listener's shelves |
| `LibraryMaintenanceWorker` | `RecommendationTuning.Maintenance.IntervalHours` | refreshes `track_stats`, prunes old events, decays transitions, removes orphans |
| `EmbeddingIndexLoader` | `RecommendationTuning.Vector.IndexReloadMinutes` | rereads the embedding matrix if anything changed |
| `AudioEmbeddingWorker` | continuous + backfill | computes missing embeddings, popular tracks first |

## Evaluating a change

```bash
make eval
```

It seeds a synthetic catalogue with scene-structured embeddings, replays a synthetic history, and
prints recall@24, precision, MAP, the home-scene share and the artist spread against a popularity
baseline. It is a measurement, not a gate: the test only asserts that the feed is not empty and
that tracks without an embedding are not skewed. The subsystem was simplified at the expense of
these numbers on purpose; read them when you change a weight.

The run is not exactly reproducible — the catalogue is seeded with `DateTimeOffset.UtcNow` and
`Guid.CreateVersion7()`, and the shelf shuffle mixes in the current UTC date. With eleven held-out
tracks one hit is nine points of recall, so treat single-hit differences as noise.

What it cannot tell you is whether CLAP hears what a listener hears. Gaussians clustered by scene
make the embeddings perfect by construction. The only real check is listening: take twenty seed
tracks, play radio from a few of them and see whether the exploration picks genuinely sound
different rather than merely unfamiliar.
