// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Application.Recommendations.Scoring;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

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

    public static string Seeded(string key, Guid seed) => $"{key}:{seed}";

    public static string BaseOf(string shelfKey)
    {
        var separator = shelfKey.IndexOf(':');
        return separator < 0 ? shelfKey : shelfKey[..separator];
    }
}

public class ShelfGenerationService(
    IApplicationDbContext db,
    CandidateGenerator generator,
    IMemoryCache memoryCache,
    IOptions<RecommendationOptions> options,
    TimeProvider clock)
{
    private const int MinimumShelfSize = 4;
    private const int MaxSeededShelves = 2;
    private RecommendationOptions Options => options.Value;

    private record Shelf(string Key, int Position, IReadOnlyList<CachedRecommendation> Items);

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


        return candidates.Count;
    }

    private async Task<List<Shelf>> BuildShelvesAsync(
        UserRecommendationContext context,
        List<RecommendationCandidate> candidates,
        CancellationToken ct)
    {
        var shelves = new List<Shelf>();
        var position = 0;

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

            if (picks.Count < MinimumShelfSize)
            {
                var wider = pool.ToList();
                picks = Explorer.Compose(wider, Options.ShelfSize, explorationRatio, Options, seed);
            }

            return picks;
        }

        var unfinished = candidates
            .Where(c => c.Source == CandidateSource.ContinueListening)
            .OrderByDescending(c => c.Score)
            .Take(Options.ShelfSize)
            .ToList();

        Add(ShelfKeys.ContinueListening, unfinished);

        Add(ShelfKeys.ForYou, Pick(candidates, ShelfKeys.ForYou, Options.ExplorationRatio));

        var similarShelf = await BuildSimilarToLastPlayedAsync(context, used, ct);
        if (similarShelf is not null)
        {
            shelves.Add(similarShelf with { Position = position++ });
            foreach (var item in similarShelf.Items)
                used.Add(item.ItemId);
        }

        foreach (var artist in context.Profile.TopArtists.Take(MaxSeededShelves))
        {
            var pool = candidates.Where(c =>
                c.ArtistIds.Contains(artist.Id) || c.ReasonSubjectId == artist.Id);

            var key = ShelfKeys.Seeded(ShelfKeys.BecauseYouListened, artist.Id);

            Add(key, Explain(
                Pick(pool, key, 0), ReasonKinds.BecauseYouListened, artist.Name, artist.Id));
        }

        var novel = candidates.Where(c => c.IsNovel).ToList();

        Add(ShelfKeys.Discover, Explain(
            Pick(novel, ShelfKeys.Discover, Options.DiscoveryExplorationRatio), ReasonKinds.Discovery));

        foreach (var genre in context.Profile.TopGenres.Take(MaxSeededShelves))
        {
            var key = ShelfKeys.Seeded(ShelfKeys.GenreMix, genre.Id);
            var picks = Pick(candidates.Where(c => c.GenreId == genre.Id), key, Options.ExplorationRatio);

            Add(key, Explain(picks, ReasonKinds.FromGenreYouLike, genre.Name, genre.Id));
        }

        var fresh = candidates
            .Where(c => c.Freshness > 0)
            .OrderByDescending(c => c.Score * (0.5 + c.Freshness))
            .ToList();

        Add(ShelfKeys.NewReleases, Explain(
            Pick(fresh, ShelfKeys.NewReleases, 0), ReasonKinds.FreshInLibrary));

        var popular = candidates
            .Where(c => c.Popularity > 0)
            .OrderByDescending(c => c.Popularity)
            .ToList();

        Add(ShelfKeys.Popular, Explain(
            Pick(popular, ShelfKeys.Popular, 0), ReasonKinds.Trending));

        AddEntityShelf(shelves, ref position, ShelfKeys.ArtistsForYou,
            AggregateBy(candidates, c => c.ArtistId, RecommendedItemKind.Artist, context));

        AddEntityShelf(shelves, ref position, ShelfKeys.AlbumsForYou,
            AggregateBy(candidates, c => c.AlbumId, RecommendedItemKind.Album, context));

        return shelves;
    }

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
        }

        db.RecommendationCache.RemoveRange(byKey.Values);

        await db.SaveChangesAsync(ct);

        memoryCache.Remove(RecommendationCacheKeys.Shelves(userId));
    }
}
