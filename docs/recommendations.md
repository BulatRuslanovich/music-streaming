# Recommendations

The largest subsystem: 83 files across `Application/Recommendations/`,
`Application/Services/Recommendations/` and `Infrastructure/Recommendations/`. This is its map.

## Two questions, two answers

Everything here exists to answer two different questions about a pair of tracks, and they are
answered by different machinery. Confusing them is the main way to get lost.

| | **Cultural**: "what does the world connect this to?" | **Sonic**: "what does this sound like?" |
|---|---|---|
| Built from | shared credits, album, genre, year, Last.fm tags, co-occurrence in sessions and playlists | a 512-dimension CLAP vector per track |
| Stored | pairwise in `track_similarity`, recomputed on a schedule | the whole matrix in RAM, never pairwise |
| Compared by | a weighted SQL formula (`Sql/score.sql`) | one dot product — the vectors are unit length |
| Code | `Infrastructure/Recommendations/SimilarityMaintenance.cs` + 8 `.sql` files | `Application/Recommendations/Embeddings/` |

A listener's taste is a point in that same 512-dimension space (`user_taste_vectors`), which is why
"how close is this track to what you like" is also one dot product.

## The path a play takes

```
client batches events
   └─► POST /api/playback/signals            (the path avoids the word "events": ad blockers eat it)
         └─► EventIngestQueue                 in-memory, the request returns 202 immediately
               └─► EventIngestWorker          persists PlaybackEvent rows
                     └─► ProfileRollupService one pass, one watermark, exactly once:
                           ├─ UserTasteProfile / affinities, with exponential decay
                           ├─ the taste vector (EMA, Recommendations:Vector:Alpha)
                           └─ the directed track_transitions graph
                                 └─► RecommendationRefreshQueue     debounced per listener
                                       └─► RecommendationWorker
                                             └─► CandidateGenerator
                                                   └─► CandidateScorer
                                                         └─► Explorer
                                                               └─► Diversifier
                                                                     └─► recommendation_cache_entries
```

The API then serves those cached rows. Generation happens in the background, hours before delivery —
which is why shelves for all four parts of the day are built together and only the matching one is
served.

## Where candidates come from

`CandidateGenerator` does not know where candidates come from. Each way of naming tracks is an
`ICandidateSource` in `Application/Recommendations/Sources/`; the generator loads the listener's
context, merges what the sources return, and materialises the result.

**The registration order in `AddCandidateSources` is behaviour, not style.** Numeric signals merge
by maximum, but the source and the explanation text ("because you listened to X") go to whichever
source named the track *first*. Reordering the registrations rewrites the captions on the shelves.
`make eval` and `RecommendationPipelineTests` are what catch it.

Sources are grouped into families (`CandidateSourceFamily`), and the multi-source bonus counts
*families*, not sources — `SimilarToRecent`, `LovedArtists` and `LovedGenres` all lean on the same
listening history, so agreeing with each other proves little. A CLAP embedding is its own family
because it knows nothing about tags, credits or who listened to what.

## What is pure and what is not

This line matters if you want to lift any of it, and it is exactly the line between the unit tests
and the Docker-requiring integration tests.

**Pure — no I/O, no framework, unit-tested:**

| Folder | Files | What |
|---|---|---|
| `Recommendations/Scoring/` | 11 | ranking weights, penalties, MMR diversification, near/far exploration, taste signals, decay |
| `Recommendations/Embeddings/` | 5 | the in-RAM matrix, vector maths, spherical k-means, the EMA fold |
| `Recommendations/Queue/` | 1 | the radio queue builder |

Their entire dependency surface is: `Domain` entities, three small option classes
(`CandidatePenaltyOptions`, `DiversityOptions`, `ExplorationOptions`) and `System.Numerics.Tensors`.
None of them references `RecommendationOptions` as a whole — that decoupling is deliberate, so the
scoring core can be read, tested and copied without dragging the project's configuration along.

**Not pure:** the 11 candidate sources, `SuppressionSet`, `TrackNeighbourLookup`, everything in
`Services/Recommendations/`, and all of `Infrastructure/Recommendations/`. These are the data
gathering, and they are the larger half by line count.

## Audio embeddings

A CLAP model under ONNX Runtime turns each track into a 512-dimension unit vector. Three 10-second
windows — start, middle, end — are averaged and normalised; the strategy token is `clap_3x10_v1`.

The model is ~280 MB and is **not** in git. Without it `IAudioEmbedder.IsAvailable` is false and
everything downstream takes the same branch a brand new library takes: no failure, just no sonic
signal. For local work without the model, set `AudioEmbedding:Provider=deterministic` — a stand-in
that hashes the file path into a vector. It knows nothing about sound, but it lets the whole path
run.

Roughly 1.5–2.5 s per track on CPU, so a large library takes hours to a day. The backfill is ordered
by popularity, so the transition period is felt on the tail of the library rather than its head.

## Maintenance passes

| Worker | Cadence | Does |
|---|---|---|
| `EventIngestWorker` | continuous | drains the event queue into rows |
| `RecommendationWorker` | debounced per listener | regenerates that listener's shelves |
| `LibraryMaintenanceWorker` | `Recommendations:Maintenance:SimilarityIntervalHours` | refreshes `track_stats`, rebuilds `track_similarity`, writes cluster labels back |
| `EmbeddingIndexLoader` | `Recommendations:Vector:IndexReloadMinutes` | rereads the embedding matrix if anything changed |
| `AudioEmbeddingWorker` | continuous + backfill | computes missing embeddings, popular tracks first |
| `ImpressionWorker` | continuous | records what was shown, for the unclicked-impression penalty |

`SimilarityMaintenance` is incremental: `track_similarity_state` holds a fingerprint of every
track's inputs, and a pass recomputes only the tracks whose fingerprint moved, plus everything they
pair with. The fingerprint is defined once, in `Sql/fingerprints.sql`, and both the "what changed"
query and the "write it back" query are built from it — if those two definitions ever drifted, the
incremental pass would silently stop converging on what a full rebuild would produce.

## Evaluating a change

```bash
make eval
```

It seeds a synthetic catalogue with scene-structured embeddings, replays a synthetic history, and
prints recall@24, precision, MAP and home-scene share against a popularity baseline.

**Read it with care.** The catalogue is seeded with `DateTimeOffset.UtcNow` and
`Guid.CreateVersion7()`, and the shelf shuffle mixes in the current UTC date, so the run is not
reproducible. Measured across five runs of identical code: recall and precision were stable, while
MAP moved by ±0.01 and the unembedded share by four percentage points. Treat recall and precision
as the signal and the rest as weather — and when comparing two versions, run both on the same day.

What it cannot tell you is whether CLAP hears what a listener hears. Gaussians clustered by scene
make the embeddings perfect by construction. The only real check is listening: take twenty seed
tracks, look at the top five sonic neighbours of each, then play radio from a few of them and see
whether the exploration picks genuinely sound different rather than merely unfamiliar.
