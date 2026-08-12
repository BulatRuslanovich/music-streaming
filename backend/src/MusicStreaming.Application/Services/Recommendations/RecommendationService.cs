using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

/// <summary>
/// The read path.
///
/// <para>
/// Everything expensive already happened in the background, so serving a shelf is a primary-key
/// read of the cached ids followed by one hydration query per kind of item. Only ids are cached:
/// titles, cover flags and the favourite marker are read fresh every time, so renaming a track or
/// hearting it shows up immediately instead of waiting for the next generation pass.
/// </para>
/// </summary>
public class RecommendationService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ShelfGenerationService generation,
    RecommendationRefreshQueue refreshQueue,
    IMemoryCache memoryCache,
    IOptions<RecommendationOptions> options,
    TimeProvider clock,
    RecommendationMetrics metrics,
    ILogger<RecommendationService> logger)
{
    /// <summary>
    /// How long the shelf ids are held in process. Short: it exists to absorb the burst of
    /// requests a page load produces, not to be a second cache tier.
    /// </summary>
    private static readonly TimeSpan MemoryCacheLifetime = TimeSpan.FromSeconds(60);

    private RecommendationOptions Options => options.Value;

    /// <summary>Builds the personal home page.</summary>
    public async Task<RecommendationHomeDto> GetHomeAsync(
        int sectionSize, bool includeScores = false, CancellationToken ct = default)
    {
        metrics.RecordRequest("home");

        var userId = currentUser.Id;
        var shelves = await LoadShelvesAsync(userId, ct);

        if (shelves.Count == 0)
            return new RecommendationHomeDto([], IsColdStart: true, GeneratedAt: null);

        var size = Math.Clamp(sectionSize, 1, Options.ShelfSize);
        var sections = await HydrateAsync(userId, shelves, size, includeScores, ct);

        var profile = await db.UserTasteProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        return new RecommendationHomeDto(
            sections,
            profile is null || profile.PositiveSignalCount == 0,
            shelves.Max(s => s.GeneratedAt));
    }

    /// <summary>
    /// The personalised track feed, paged.
    ///
    /// <para>
    /// Drawn from every cached track shelf rather than from one of them: the shelves are already
    /// the ranked, de-duplicated, diversified result, and merging them by score gives a longer
    /// feed without a second generation strategy that could disagree with the first.
    /// </para>
    /// </summary>
    public async Task<PagedResult<RecommendedTrackDto>> GetTracksAsync(
        PageRequest page, bool includeScores = false, CancellationToken ct = default)
    {
        metrics.RecordRequest("tracks");

        var userId = currentUser.Id;
        var shelves = await LoadShelvesAsync(userId, ct);

        var ranked = shelves
            .SelectMany(shelf => shelf.Payload)
            .Where(item => item.Kind == RecommendedItemKind.Track)
            .GroupBy(item => item.ItemId)
            .Select(group => group.OrderByDescending(item => item.Score).First())
            .OrderByDescending(item => item.Score)
            .ToList();

        var pageItems = ranked.Skip(page.Skip).Take(page.PageSize).ToList();
        var tracks = await LoadTracksAsync(userId, pageItems.Select(i => i.ItemId), ct);

        var items = pageItems
            .Where(item => tracks.ContainsKey(item.ItemId))
            .Select(item => ToDto(tracks[item.ItemId], item, includeScores))
            .ToList();

        return new PagedResult<RecommendedTrackDto>(items, ranked.Count, page.Page, page.PageSize);
    }

    /// <summary>Recommended artists.</summary>
    public async Task<IReadOnlyList<ArtistDto>> GetArtistsAsync(int limit, CancellationToken ct = default)
    {
        metrics.RecordRequest("artists");
        return await GetEntitiesAsync(
            ShelfKeys.ArtistsForYou, RecommendedItemKind.Artist, limit, LoadArtistsAsync, ct);
    }

    /// <summary>Recommended albums.</summary>
    public async Task<IReadOnlyList<AlbumDto>> GetAlbumsAsync(int limit, CancellationToken ct = default)
    {
        metrics.RecordRequest("albums");
        return await GetEntitiesAsync(
            ShelfKeys.AlbumsForYou, RecommendedItemKind.Album, limit, LoadAlbumsAsync, ct);
    }

    /// <summary>
    /// Tracks similar to a given one.
    ///
    /// <para>
    /// Falls back to the track's own artist and genre when the similarity table has nothing for it
    /// — which is the normal state of a track uploaded five minutes ago, and of any track at all
    /// before the first maintenance pass has run. An empty list would read as "nothing is like
    /// this", which is almost never true.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<RecommendedTrackDto>> GetSimilarAsync(
        Guid trackId, int limit, bool includeScores = false, CancellationToken ct = default)
    {
        metrics.RecordRequest("similar");

        var seed = await db.Tracks.AsNoTracking()
            .Where(t => t.Id == trackId)
            .Select(t => new { t.Id, t.Title, t.ArtistId, t.GenreId })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Track not found.");

        var size = Math.Clamp(limit, 1, PageRequest.MaxPageSize);

        var neighbours = await db.TrackSimilarities.AsNoTracking()
            .Where(s => s.TrackId == trackId)
            .OrderByDescending(s => s.Score)
            .Take(size)
            .Select(s => new { s.SimilarTrackId, s.Score })
            .ToListAsync(ct);

        var scores = neighbours.ToDictionary(n => n.SimilarTrackId, n => n.Score);
        var order = neighbours.Select(n => n.SimilarTrackId).ToList();

        if (order.Count == 0)
        {
            order = await db.Tracks.AsNoTracking()
                .Where(t => t.Id != trackId
                            && (t.TrackArtists.Any(ta => ta.ArtistId == seed.ArtistId)
                                || (seed.GenreId != null && t.GenreId == seed.GenreId)))
                .OrderByDescending(t => t.ArtistId == seed.ArtistId)
                .ThenByDescending(t => t.CreatedAt)
                .Take(size)
                .Select(t => t.Id)
                .ToListAsync(ct);
        }

        var tracks = await LoadTracksAsync(currentUser.Id, order, ct);
        var reason = new RecommendationReasonDto(ReasonKinds.SimilarTo, seed.Title, seed.Id);

        return order
            .Where(tracks.ContainsKey)
            .Select(id => new RecommendedTrackDto(
                tracks[id], reason, includeScores ? scores.GetValueOrDefault(id) : null))
            .ToList();
    }

    // ── Shelf loading ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the user's cached shelves, generating them inline only when there are none at all.
    ///
    /// <para>
    /// Expired shelves are served as they are and a rebuild is queued. Stale recommendations are
    /// a far smaller problem than a home page that blocks for a second while it thinks.
    /// </para>
    /// </summary>
    private async Task<List<RecommendationCacheEntry>> LoadShelvesAsync(Guid userId, CancellationToken ct)
    {
        var cacheKey = RecommendationCacheKeys.Shelves(userId);

        if (memoryCache.TryGetValue(cacheKey, out List<RecommendationCacheEntry>? cached) && cached is not null)
        {
            metrics.RecordCacheHit("memory");
            return cached;
        }

        var shelves = await ReadShelvesAsync(userId, ct);

        if (shelves.Count == 0)
        {
            metrics.RecordCacheMiss("empty");
            shelves = await GenerateInlineAsync(userId, ct);
        }
        else
        {
            var now = clock.GetUtcNow();
            metrics.RecordCacheHit("database");

            if (shelves.Any(s => s.ExpiresAt <= now))
                refreshQueue.MarkDirty(userId, now);
        }

        memoryCache.Set(cacheKey, shelves, MemoryCacheLifetime);
        return shelves;
    }

    private async Task<List<RecommendationCacheEntry>> ReadShelvesAsync(Guid userId, CancellationToken ct) =>
        await db.RecommendationCache.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Position)
            .ToListAsync(ct);

    /// <summary>
    /// Builds shelves during a request. Only reached on a user's very first visit, or after the
    /// cache has been cleared — every other path is served from the background pass.
    /// </summary>
    private async Task<List<RecommendationCacheEntry>> GenerateInlineAsync(Guid userId, CancellationToken ct)
    {
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var run = new RecommendationRun
        {
            UserId = userId,
            Trigger = RecommendationTrigger.OnDemand,
            StartedAt = clock.GetUtcNow(),
            Status = RecommendationRunStatus.Succeeded,
        };

        try
        {
            run.CandidateCount = await generation.GenerateAsync(userId, run.Id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A listener asking for their home page should get the rest of the page, not a 500.
            logger.LogError(ex, "Inline recommendation generation failed for user {UserId}", userId);
            return [];
        }

        var shelves = await ReadShelvesAsync(userId, ct);

        run.ShelfCount = shelves.Count;
        run.DurationMs = (int)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

        db.RecommendationRuns.Add(run);
        await db.SaveChangesAsync(ct);

        metrics.RecordGeneration(
            System.Diagnostics.Stopwatch.GetElapsedTime(startedAt), run.CandidateCount);

        return shelves;
    }

    // ── Hydration ───────────────────────────────────────────────────────────────────────────

    private async Task<List<RecommendationSectionDto>> HydrateAsync(
        Guid userId,
        List<RecommendationCacheEntry> shelves,
        int sectionSize,
        bool includeScores,
        CancellationToken ct)
    {
        var wanted = shelves
            .SelectMany(shelf => shelf.Payload.Take(sectionSize).Select(item => (shelf, item)))
            .ToList();

        var tracks = await LoadTracksAsync(
            userId, Ids(wanted, RecommendedItemKind.Track), ct);
        var artists = await LoadArtistsAsync(Ids(wanted, RecommendedItemKind.Artist), ct);
        var albums = await LoadAlbumsAsync(Ids(wanted, RecommendedItemKind.Album), ct);

        var sections = new List<RecommendationSectionDto>(shelves.Count);

        foreach (var shelf in shelves)
        {
            var items = shelf.Payload.Take(sectionSize).ToList();
            if (items.Count == 0)
                continue;

            var reason = ReasonOf(items[0]);
            var section = items[0].Kind switch
            {
                RecommendedItemKind.Artist => new RecommendationSectionDto(
                    shelf.ShelfKey, ShelfKeys.BaseOf(shelf.ShelfKey), reason, null,
                    Resolve(items, artists), null),

                RecommendedItemKind.Album => new RecommendationSectionDto(
                    shelf.ShelfKey, ShelfKeys.BaseOf(shelf.ShelfKey), reason, null, null,
                    Resolve(items, albums)),

                _ => new RecommendationSectionDto(
                    shelf.ShelfKey, ShelfKeys.BaseOf(shelf.ShelfKey), reason,
                    items.Where(item => tracks.ContainsKey(item.ItemId))
                        .Select(item => ToDto(tracks[item.ItemId], item, includeScores))
                        .ToList(),
                    null, null),
            };

            // A shelf can empty out between generation and now: tracks get deleted. Dropping the
            // heading is better than showing an empty row.
            if (SectionIsEmpty(section))
                continue;

            sections.Add(section);
        }

        return sections;
    }

    private async Task<IReadOnlyList<T>> GetEntitiesAsync<T>(
        string shelfKey,
        RecommendedItemKind kind,
        int limit,
        Func<IEnumerable<Guid>, CancellationToken, Task<Dictionary<Guid, T>>> loader,
        CancellationToken ct)
    {
        var shelves = await LoadShelvesAsync(currentUser.Id, ct);

        var items = shelves
            .Where(shelf => shelf.ShelfKey == shelfKey)
            .SelectMany(shelf => shelf.Payload)
            .Where(item => item.Kind == kind)
            .Take(Math.Clamp(limit, 1, PageRequest.MaxPageSize))
            .ToList();

        if (items.Count == 0)
            return [];

        var loaded = await loader(items.Select(item => item.ItemId), ct);
        return Resolve(items, loaded);
    }

    private static IEnumerable<Guid> Ids(
        List<(RecommendationCacheEntry Shelf, CachedRecommendation Item)> wanted, RecommendedItemKind kind) =>
        wanted.Where(w => w.Item.Kind == kind).Select(w => w.Item.ItemId).Distinct();

    /// <summary>Keeps the cached order, dropping anything that has since been deleted.</summary>
    private static List<T> Resolve<T>(List<CachedRecommendation> items, Dictionary<Guid, T> loaded) =>
        items.Where(item => loaded.ContainsKey(item.ItemId)).Select(item => loaded[item.ItemId]).ToList();

    private static bool SectionIsEmpty(RecommendationSectionDto section) =>
        (section.Tracks?.Count ?? 0) == 0
        && (section.Artists?.Count ?? 0) == 0
        && (section.Albums?.Count ?? 0) == 0;

    private static RecommendedTrackDto ToDto(TrackDto track, CachedRecommendation item, bool includeScores) =>
        new(track, ReasonOf(item), includeScores ? item.Score : null);

    private static RecommendationReasonDto ReasonOf(CachedRecommendation item) =>
        new(item.ReasonKind, item.ReasonSubject, item.ReasonSubjectId);

    private async Task<Dictionary<Guid, TrackDto>> LoadTracksAsync(
        Guid userId, IEnumerable<Guid> trackIds, CancellationToken ct)
    {
        var ids = trackIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        return await db.Tracks.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .Select(Projections.Track(userId))
            .ToDictionaryAsync(t => t.Id, ct);
    }

    private async Task<Dictionary<Guid, ArtistDto>> LoadArtistsAsync(
        IEnumerable<Guid> artistIds, CancellationToken ct)
    {
        var ids = artistIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        return await db.Artists.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(Projections.Artist)
            .ToDictionaryAsync(a => a.Id, ct);
    }

    private async Task<Dictionary<Guid, AlbumDto>> LoadAlbumsAsync(
        IEnumerable<Guid> albumIds, CancellationToken ct)
    {
        var ids = albumIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        return await db.Albums.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(Projections.Album)
            .ToDictionaryAsync(a => a.Id, ct);
    }
}
