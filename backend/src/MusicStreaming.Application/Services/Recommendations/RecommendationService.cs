// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Collections.Concurrent;
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

public class RecommendationService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ShelfGenerationService generation,
    CandidateGenerator generator,
    RecommendationRefreshQueue refreshQueue,
    IMemoryCache memoryCache,
    IOptions<RecommendationOptions> options,
    TimeProvider clock,
    RecommendationMetrics metrics,
    ILogger<RecommendationService> logger)
{
    private static readonly TimeSpan MemoryCacheLifetime = TimeSpan.FromSeconds(60);
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> InlineBuilds = new();
    private RecommendationOptions Options => options.Value;

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
        var tracks = await db.TracksByIdAsync(userId, pageItems.Select(i => i.ItemId), ct);

        var items = pageItems
            .Where(item => tracks.ContainsKey(item.ItemId))
            .Select(item => ToDto(tracks[item.ItemId], item, includeScores))
            .ToList();

        return new PagedResult<RecommendedTrackDto>(items, ranked.Count, page.Page, page.PageSize);
    }

    public async Task<IReadOnlyList<ArtistDto>> GetArtistsAsync(int limit, CancellationToken ct = default)
    {
        metrics.RecordRequest("artists");
        return await GetEntitiesAsync(
            ShelfKeys.ArtistsForYou, RecommendedItemKind.Artist, limit, db.ArtistsByIdAsync, ct);
    }

    public async Task<IReadOnlyList<AlbumDto>> GetAlbumsAsync(int limit, CancellationToken ct = default)
    {
        metrics.RecordRequest("albums");
        return await GetEntitiesAsync(
            ShelfKeys.AlbumsForYou, RecommendedItemKind.Album, limit, db.AlbumsByIdAsync, ct);
    }

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
            order = [.. await generator.SameArtistOrGenreAsync(trackId, size, ct)];

        var tracks = await db.TracksByIdAsync(currentUser.Id, order, ct);
        var reason = new RecommendationReasonDto(ReasonKinds.SimilarTo, seed.Title, seed.Id);

        return order
            .Where(tracks.ContainsKey)
            .Select(id => new RecommendedTrackDto(
                tracks[id], reason, includeScores ? scores.GetValueOrDefault(id) : null))
            .ToList();
    }

    /// <summary>
    /// Что предложить добавить в плейлист. Умышленно не переиспользует DJ-маршрут: тот выкидывает
    /// всё, что играло за последние сутки, пишет <c>RecommendationImpression</c> на каждый показ и
    /// молчит при выключенном автоплее — для страницы плейлиста всё это неверно.
    /// </summary>
    /// <param name="seedTrackIds">Треки плейлиста: и затравка, и список исключений.</param>
    public async Task<IReadOnlyList<RecommendedTrackDto>> SuggestForTracksAsync(
        IReadOnlyList<Guid> seedTrackIds, int limit, CancellationToken ct = default)
    {
        metrics.RecordRequest("playlistSuggestions");

        var size = Math.Clamp(limit, 1, PageRequest.MaxPageSize);
        var exclude = seedTrackIds.ToHashSet();

        var neighbours = seedTrackIds.Count == 0
            ? []
            : await db.TrackSimilarities.AsNoTracking()
                .Where(s => seedTrackIds.Contains(s.TrackId))
                .GroupBy(s => s.SimilarTrackId)
                .OrderByDescending(g => g.Sum(s => s.Score))
                .Take(size * 2)
                .Select(g => g.Key)
                .ToListAsync(ct);

        var order = neighbours.Where(id => !exclude.Contains(id)).Take(size).ToList();

        // Пустой плейлист (или недобор соседей) добирается персональной выдачей — для только что
        // созданного плейлиста это и есть правильный ответ, и никакой новой машинерии не нужно.
        if (order.Count < size)
        {
            var personal = await GetTracksAsync(new PageRequest(1, size * 2), false, ct);

            order.AddRange(personal.Items
                .Select(item => item.Track.Id)
                .Where(id => !exclude.Contains(id) && !order.Contains(id))
                .Take(size - order.Count));
        }

        var tracks = await db.TracksByIdAsync(currentUser.Id, order, ct);
        var reason = new RecommendationReasonDto(ReasonKinds.SimilarTo, null, null);

        return [.. order
            .Where(tracks.ContainsKey)
            .Select(id => new RecommendedTrackDto(tracks[id], reason, null))];
    }

    private const int SimilarArtistSeedTracks = 40;

    /// <summary>
    /// Отдельной таблицы похожести артистов нет и не нужно: рёбра <c>TrackSimilarity</c> уже
    /// посчитаны, и похожесть артистов получается их суммированием по авторам похожих треков.
    /// </summary>
    /// <remarks>
    /// Рёбра существуют только при включённых рекомендациях, поэтому пустой результат — штатная
    /// ситуация (свежая база, выключённый воркер), а не ошибка. На этот случай есть фолбэк по
    /// жанру: секция на странице артиста должна оставаться осмысленной без всякой аналитики.
    /// </remarks>
    public async Task<IReadOnlyList<ArtistDto>> GetSimilarArtistsAsync(
        Guid artistId, int limit, CancellationToken ct = default)
    {
        metrics.RecordRequest("similarArtists");

        if (!await db.Artists.AnyAsync(a => a.Id == artistId, ct))
            throw new NotFoundException("Artist not found.");

        var size = Math.Clamp(limit, 1, PageRequest.MaxPageSize);

        var seedIds = await db.Tracks.AsNoTracking()
            .Where(t => t.TrackArtists.Any(ta => ta.ArtistId == artistId))
            .OrderByDescending(t => t.Stats == null ? 0 : t.Stats.PopularityScore)
            .Take(SimilarArtistSeedTracks)
            .Select(t => t.Id)
            .ToListAsync(ct);

        var order = seedIds.Count == 0
            ? []
            : await db.TrackSimilarities.AsNoTracking()
                .Where(s => seedIds.Contains(s.TrackId))
                .SelectMany(s => s.SimilarTrack!.TrackArtists.Select(ta => new { ta.ArtistId, s.Score }))
                .Where(x => x.ArtistId != artistId)
                .GroupBy(x => x.ArtistId)
                .OrderByDescending(g => g.Sum(x => x.Score))
                .Take(size)
                .Select(g => g.Key)
                .ToListAsync(ct);

        if (order.Count == 0)
            order = await SameGenreArtistsAsync(artistId, size, ct);

        var artists = await db.ArtistsByIdAsync(order, ct);

        return [.. order.Where(artists.ContainsKey).Select(id => artists[id])];
    }

    /// <summary>Фолбэк без аналитики: соседи по доминирующему жанру, самые крупные сверху.</summary>
    private async Task<List<Guid>> SameGenreArtistsAsync(Guid artistId, int size, CancellationToken ct)
    {
        var genreId = await db.Tracks.AsNoTracking()
            .Where(t => t.TrackArtists.Any(ta => ta.ArtistId == artistId) && t.GenreId != null)
            .GroupBy(t => t.GenreId!.Value)
            .OrderByDescending(g => g.Count())
            .Select(g => (Guid?)g.Key)
            .FirstOrDefaultAsync(ct);

        if (genreId is null)
            return [];

        return await db.Artists.AsNoTracking()
            .Where(a => a.Id != artistId
                        && a.TrackCredits.Any(tc => tc.Track!.GenreId == genreId))
            .OrderByDescending(a => a.TrackCredits.Count)
            .Take(size)
            .Select(a => a.Id)
            .ToListAsync(ct);
    }

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
            shelves = await BuildOnceAsync(userId, ct);
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

    private async Task<List<RecommendationCacheEntry>> BuildOnceAsync(Guid userId, CancellationToken ct)
    {
        var gate = InlineBuilds.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(ct);
        try
        {
            var built = await ReadShelvesAsync(userId, ct);
            if (built.Count > 0)
            {
                metrics.RecordCacheHit("database");
                return built;
            }

            return await GenerateInlineAsync(userId, ct);
        }
        finally
        {
            gate.Release();
        }
    }

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

        var tracks = await db.TracksByIdAsync(userId, Ids(wanted, RecommendedItemKind.Track), ct);
        var artists = await db.ArtistsByIdAsync(Ids(wanted, RecommendedItemKind.Artist), ct);
        var albums = await db.AlbumsByIdAsync(Ids(wanted, RecommendedItemKind.Album), ct);

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

}
