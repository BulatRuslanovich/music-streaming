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
    ShelfHydrator hydrator,
    TrackNeighbourLookup neighbourLookup,
    RecommendationRefreshQueue refreshQueue,
    IMemoryCache memoryCache,
    IOptions<RecommendationOptions> options,
    TimeProvider clock,
    RecommendationMetrics metrics,
    ILogger<RecommendationService> logger)
{
    private static readonly TimeSpan MemoryCacheLifetime = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan TimeZoneCacheLifetime = TimeSpan.FromMinutes(10);
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
        var sections = await hydrator.HydrateAsync(userId, shelves, size, includeScores, ct);

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
            .Select(item => ShelfHydrator.ToDto(tracks[item.ItemId], item, includeScores))
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
            order = [.. await neighbourLookup.SameArtistOrGenreAsync(trackId, size, ct)];

        var tracks = await db.TracksByIdAsync(currentUser.Id, order, ct);
        var reason = new RecommendationReasonDto(ReasonKinds.SimilarTo, seed.Title, seed.Id);

        return order
            .Where(tracks.ContainsKey)
            .Select(id => new RecommendedTrackDto(
                tracks[id], reason, includeScores ? scores.GetValueOrDefault(id) : null))
            .ToList();
    }

    /// <summary>
    /// Полки на все части суток лежат в кэше, но отдаётся только та, что совпадает с местным
    /// временем слушателя: фильтр стоит на отдаче, потому что генерация идёт за часы до неё.
    /// </summary>
    private async Task<List<RecommendationCacheEntry>> LoadShelvesAsync(Guid userId, CancellationToken ct)
    {
        var shelves = await LoadAllShelvesAsync(userId, ct);
        var current = Dayparts.Of(clock.GetUtcNow(), await TimeZoneAsync(userId, ct));

        return shelves
            .Where(shelf => ShelfKeys.DaypartOf(shelf.ShelfKey) is not { } part || part == current)
            .ToList();
    }

    private async Task<TimeZoneInfo> TimeZoneAsync(Guid userId, CancellationToken ct)
    {
        var cacheKey = $"recommendations:timezone:{userId}";

        if (memoryCache.TryGetValue(cacheKey, out TimeZoneInfo? cached) && cached is not null)
            return cached;

        var zone = Dayparts.ZoneOrUtc(await db.UserSettings.AsNoTracking()
            .Where(item => item.UserId == userId)
            .Select(item => item.TimeZone)
            .FirstOrDefaultAsync(ct));

        memoryCache.Set(cacheKey, zone, TimeZoneCacheLifetime);

        return zone;
    }

    private async Task<List<RecommendationCacheEntry>> LoadAllShelvesAsync(Guid userId, CancellationToken ct)
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
        return ShelfHydrator.Resolve(items, loaded);
    }

}
