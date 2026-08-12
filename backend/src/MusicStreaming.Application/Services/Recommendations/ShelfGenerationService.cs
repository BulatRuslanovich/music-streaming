using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Application.Recommendations.Scoring;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

/// <summary>The shelf keys the home page is built from.</summary>
public static class ShelfKeys
{
    public const string ContinueListening = "continueListening";
    public const string ForYou = "forYou";
    public const string SimilarTo = "similarTo";
    public const string BecauseYouListened = "becauseYouListened";
    public const string Discover = "discover";
    public const string GenreMix = "genreMix";
    public const string NewReleases = "newReleases";
    public const string Popular = "popular";
    public const string ArtistsForYou = "artistsForYou";
    public const string AlbumsForYou = "albumsForYou";

    /// <summary>Builds a seeded key, e.g. <c>similarTo:1f2e…</c>.</summary>
    public static string Seeded(string key, Guid seed) => $"{key}:{seed}";

    /// <summary>The part before the seed, which is what the client maps to a heading.</summary>
    public static string BaseOf(string shelfKey)
    {
        var separator = shelfKey.IndexOf(':');
        return separator < 0 ? shelfKey : shelfKey[..separator];
    }
}

/// <summary>
/// Builds every shelf of one user's personal home page and stores the result.
///
/// <para>
/// This is the expensive half of the engine, and it runs in the background precisely so that the
/// read path does not have to: by the time a request arrives, the answer is a row in a table.
/// </para>
/// </summary>
public class ShelfGenerationService(
    IApplicationDbContext db,
    CandidateGenerator generator,
    IMemoryCache memoryCache,
    IOptions<RecommendationOptions> options,
    TimeProvider clock,
    RecommendationMetrics metrics,
    ILogger<ShelfGenerationService> logger)
{
    /// <summary>Below this a shelf looks broken, so it is dropped instead of shown.</summary>
    private const int MinimumShelfSize = 4;

    private const int MaxSeededShelves = 2;

    private RecommendationOptions Options => options.Value;

    /// <summary>A finished shelf, ready to be cached.</summary>
    private record Shelf(string Key, int Position, IReadOnlyList<CachedRecommendation> Items);

    /// <summary>Rebuilds and stores every shelf. Returns the number of candidates considered.</summary>
    public async Task<int> GenerateAsync(Guid userId, Guid runId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();

        var context = await generator.LoadContextAsync(userId, now, ct);
        var candidates = await generator.GenerateAsync(context, ct);

        var weights = Options.WeightsFor(context.Profile.Maturity);
        foreach (var candidate in candidates)
            CandidateScorer.Score(candidate, context.Ranking, weights, Options);

        var shelves = await BuildShelvesAsync(context, candidates, ct);
        await PersistAsync(userId, runId, shelves, now, ct);

        logger.LogDebug(
            "Built {Shelves} shelves for user {UserId} from {Candidates} candidates ({Maturity})",
            shelves.Count, userId, candidates.Count, context.Profile.Maturity);

        return candidates.Count;
    }

    private async Task<List<Shelf>> BuildShelvesAsync(
        UserRecommendationContext context,
        List<RecommendationCandidate> candidates,
        CancellationToken ct)
    {
        var shelves = new List<Shelf>();
        var position = 0;

        // Tracks already placed. Later shelves avoid them so that one home page does not show the
        // same song three times — but a shelf that would fall below the minimum takes them back,
        // because a coherent short shelf beats a missing one.
        var used = new HashSet<Guid>();

        void Add(string key, IReadOnlyList<RecommendationCandidate> picks)
        {
            if (picks.Count < MinimumShelfSize)
                return;

            shelves.Add(new Shelf(key, position++, picks.Select(ToCached).ToList()));

            foreach (var pick in picks)
                used.Add(pick.TrackId);
        }

        List<RecommendationCandidate> Pick(
            IEnumerable<RecommendationCandidate> pool, string shelfKey, double explorationRatio)
        {
            var available = pool.Where(c => !used.Contains(c.TrackId)).ToList();
            var seed = Explorer.SeedFor(context.UserId, shelfKey, context.Ranking.Now);

            var picks = Explorer.Compose(available, Options.ShelfSize, explorationRatio, Options, seed);

            // Not enough left after de-duplication: allow the repeats rather than drop the shelf.
            if (picks.Count < MinimumShelfSize)
            {
                var wider = pool.ToList();
                picks = Explorer.Compose(wider, Options.ShelfSize, explorationRatio, Options, seed);
            }

            return picks;
        }

        // 1. Where the user left off. No exploration: this shelf has exactly one job.
        var unfinished = candidates
            .Where(c => c.Source == CandidateSource.ContinueListening)
            .OrderByDescending(c => c.Score)
            .Take(Options.ShelfSize)
            .ToList();

        Add(ShelfKeys.ContinueListening, unfinished);

        // 2. The main personalised mix.
        Add(ShelfKeys.ForYou, Pick(candidates, ShelfKeys.ForYou, Options.ExplorationRatio));

        // 3. Neighbours of the track played most recently.
        var similarShelf = await BuildSimilarToLastPlayedAsync(context, used, ct);
        if (similarShelf is not null)
        {
            shelves.Add(similarShelf with { Position = position++ });
            foreach (var item in similarShelf.Items)
                used.Add(item.ItemId);
        }

        // 4. One shelf per favourite artist — their own tracks and the neighbours they led to.
        foreach (var artist in context.Profile.TopArtists.Take(MaxSeededShelves))
        {
            var pool = candidates.Where(c =>
                c.ArtistIds.Contains(artist.Id) || c.ReasonSubjectId == artist.Id);

            var key = ShelfKeys.Seeded(ShelfKeys.BecauseYouListened, artist.Id);

            Add(key, Explain(
                Pick(pool, key, 0), ReasonKinds.BecauseYouListened, artist.Name, artist.Id));
        }

        // 5. Deliberately unfamiliar. Exploration is the point, so it is turned up.
        var novel = candidates.Where(c => c.IsNovel).ToList();

        Add(ShelfKeys.Discover, Explain(
            Pick(novel, ShelfKeys.Discover, Options.DiscoveryExplorationRatio), ReasonKinds.Discovery));

        // 6. A mix per favourite genre.
        foreach (var genre in context.Profile.TopGenres.Take(MaxSeededShelves))
        {
            var key = ShelfKeys.Seeded(ShelfKeys.GenreMix, genre.Id);
            var picks = Pick(candidates.Where(c => c.GenreId == genre.Id), key, Options.ExplorationRatio);

            Add(key, Explain(picks, ReasonKinds.FromGenreYouLike, genre.Name, genre.Id));
        }

        // 7. Newest in the library, ranked by how well they fit this listener.
        var fresh = candidates
            .Where(c => c.Freshness > 0)
            .OrderByDescending(c => c.Score * (0.5 + c.Freshness))
            .ToList();

        Add(ShelfKeys.NewReleases, Explain(
            Pick(fresh, ShelfKeys.NewReleases, 0), ReasonKinds.FreshInLibrary));

        // 8. What the whole library is playing.
        var popular = candidates
            .Where(c => c.Popularity > 0)
            .OrderByDescending(c => c.Popularity)
            .ToList();

        Add(ShelfKeys.Popular, Explain(
            Pick(popular, ShelfKeys.Popular, 0), ReasonKinds.Trending));

        // 9 and 10. The same pool, viewed by artist and by album.
        AddEntityShelf(shelves, ref position, ShelfKeys.ArtistsForYou,
            AggregateBy(candidates, c => c.ArtistId, RecommendedItemKind.Artist, context));

        AddEntityShelf(shelves, ref position, ShelfKeys.AlbumsForYou,
            AggregateBy(candidates, c => c.AlbumId, RecommendedItemKind.Album, context));

        return shelves;
    }

    /// <summary>
    /// "More like the last thing you played." Read straight from the similarity table rather than
    /// filtered out of the candidate pool, because the pool is capped and the single most relevant
    /// neighbour of one specific track may well not be in it.
    /// </summary>
    private async Task<Shelf?> BuildSimilarToLastPlayedAsync(
        UserRecommendationContext context, HashSet<Guid> used, CancellationToken ct)
    {
        var lastPlayed = context.Ranking.History
            .Where(pair => pair.Value.Score > 0)
            .OrderByDescending(pair => pair.Value.LastPlayedAt)
            .Select(pair => (Guid?)pair.Key)
            .FirstOrDefault();

        if (lastPlayed is not { } seedId)
            return null;

        var seed = await db.Tracks.AsNoTracking()
            .Where(t => t.Id == seedId)
            .Select(t => new { t.Title })
            .FirstOrDefaultAsync(ct);

        if (seed is null)
            return null;

        var neighbours = await db.TrackSimilarities.AsNoTracking()
            .Where(s => s.TrackId == seedId)
            .OrderByDescending(s => s.Score)
            .Take(Options.ShelfSize * 2)
            .Select(s => new { s.SimilarTrackId, s.Score })
            .ToListAsync(ct);

        var unused = neighbours.Where(n => !used.Contains(n.SimilarTrackId)).ToList();

        // Same rule as the other shelves: a small library can exhaust the unused pool, and
        // repeating a track elsewhere on the page beats dropping the shelf entirely.
        if (unused.Count < MinimumShelfSize)
            unused = neighbours;

        var items = unused
            .Take(Options.ShelfSize)
            .Select(n => new CachedRecommendation(
                n.SimilarTrackId, RecommendedItemKind.Track, n.Score,
                ReasonKinds.SimilarTo, seed.Title, seedId))
            .ToList();

        return items.Count < MinimumShelfSize
            ? null
            : new Shelf(ShelfKeys.Seeded(ShelfKeys.SimilarTo, seedId), 0, items);
    }

    private static void AddEntityShelf(
        List<Shelf> shelves,
        ref int position,
        string key,
        List<CachedRecommendation> items)
    {
        if (items.Count < MinimumShelfSize)
            return;

        shelves.Add(new Shelf(key, position++, items));
    }

    /// <summary>
    /// Rolls the track pool up to artists or albums. The strongest track an artist has in the pool
    /// stands for the artist, so a name recommended here always has something behind it.
    /// </summary>
    private List<CachedRecommendation> AggregateBy(
        List<RecommendationCandidate> candidates,
        Func<RecommendationCandidate, Guid?> selector,
        RecommendedItemKind kind,
        UserRecommendationContext context)
    {
        var grouped = new Dictionary<Guid, (double Score, string Reason, string? Subject, Guid? SubjectId)>();

        foreach (var candidate in candidates)
        {
            if (selector(candidate) is not { } id || id == Guid.Empty)
                continue;

            if (!grouped.TryGetValue(id, out var existing) || candidate.Score > existing.Score)
            {
                grouped[id] = (
                    candidate.Score,
                    candidate.ReasonKind,
                    candidate.ReasonSubject,
                    candidate.ReasonSubjectId);
            }
        }

        // An artist the user already plays constantly is not a recommendation. Their established
        // favourites are dropped so the shelf says something they do not already know.
        var establishedArtists = context.Profile.TopArtists.Take(3).Select(a => a.Id).ToHashSet();

        return grouped
            .Where(pair => kind != RecommendedItemKind.Artist || !establishedArtists.Contains(pair.Key))
            .OrderByDescending(pair => pair.Value.Score)
            .Take(Options.ShelfSize)
            .Select(pair => new CachedRecommendation(
                pair.Key, kind, pair.Value.Score,
                pair.Value.Reason, pair.Value.Subject, pair.Value.SubjectId))
            .ToList();
    }

    /// <summary>
    /// Overrides the per-candidate explanations on a shelf whose heading already states the
    /// reason. "New in your library" must not be filed under the genre one of its tracks happened
    /// to be found through.
    /// </summary>
    private static List<RecommendationCandidate> Explain(
        List<RecommendationCandidate> picks, string reasonKind, string? subject = null, Guid? subjectId = null)
    {
        foreach (var pick in picks)
        {
            pick.ReasonKind = reasonKind;
            pick.ReasonSubject = subject;
            pick.ReasonSubjectId = subjectId;
        }

        return picks;
    }

    private static CachedRecommendation ToCached(RecommendationCandidate candidate) => new(
        candidate.TrackId,
        RecommendedItemKind.Track,
        candidate.Score,
        candidate.ReasonKind,
        candidate.ReasonSubject,
        candidate.ReasonSubjectId);

    /// <summary>
    /// Replaces the user's cached shelves and records what was shown.
    ///
    /// <para>
    /// Impressions are written here, at generation time, rather than when a shelf is served. The
    /// read path stays read-only, and the cooldown works on what was actually put in front of the
    /// user, which is what a generated shelf is.
    /// </para>
    /// </summary>
    private async Task PersistAsync(
        Guid userId, Guid runId, List<Shelf> shelves, DateTimeOffset now, CancellationToken ct)
    {
        var expiresAt = now.AddHours(Options.CacheTtlHours);

        var existing = await db.RecommendationCache
            .Where(c => c.UserId == userId)
            .ToListAsync(ct);

        var byKey = existing.ToDictionary(c => c.ShelfKey);

        foreach (var shelf in shelves)
        {
            if (byKey.Remove(shelf.Key, out var entry))
            {
                entry.Payload = shelf.Items;
                entry.Position = shelf.Position;
                entry.GeneratedAt = now;
                entry.ExpiresAt = expiresAt;
                entry.RunId = runId;
            }
            else
            {
                db.RecommendationCache.Add(new RecommendationCacheEntry
                {
                    UserId = userId,
                    ShelfKey = shelf.Key,
                    Position = shelf.Position,
                    Payload = shelf.Items,
                    GeneratedAt = now,
                    ExpiresAt = expiresAt,
                    RunId = runId,
                });
            }

            RecordImpressions(userId, shelf, now);
        }

        // Shelves that no longer apply — a genre that fell out of favour, a seed track deleted.
        db.RecommendationCache.RemoveRange(byKey.Values);

        await db.SaveChangesAsync(ct);

        // The in-process cache in front of these rows would otherwise keep serving the previous
        // shelves for its lifetime, so a rebuild triggered by something the user just did would
        // appear to have done nothing.
        memoryCache.Remove(RecommendationCacheKeys.Shelves(userId));
    }

    private void RecordImpressions(Guid userId, Shelf shelf, DateTimeOffset now)
    {
        var position = 0;

        foreach (var item in shelf.Items)
        {
            if (item.Kind != RecommendedItemKind.Track)
                continue;

            db.RecommendationImpressions.Add(new RecommendationImpression
            {
                UserId = userId,
                TrackId = item.ItemId,
                ShelfKey = shelf.Key,
                Position = position++,
                ShownAt = now,
            });
        }

        metrics.RecordImpressions(position, ShelfKeys.BaseOf(shelf.Key));
    }
}
