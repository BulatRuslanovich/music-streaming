// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

public class RecommendationService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ShelfGenerationService generation,
    ShelfHydrator hydrator,
    RecommendationRefreshQueue refreshQueue,
    InlineBuildGate inlineBuilds,
    IMemoryCache memoryCache,
    TimeProvider clock,
    ILogger<RecommendationService> logger)
{
    private static readonly TimeSpan MemoryCacheLifetime = TimeSpan.FromSeconds(60);

    /// <summary>The shelves the home page shows; the hidden daily mix pool is left out.</summary>
    public async Task<RecommendationHomeDto> GetHomeAsync(
        int sectionSize,
        bool includeScores = false,
        CancellationToken ct = default)
    {
        var userId = currentUser.Id;
        var shelves = await LoadShelvesAsync(userId, ct);

        if (shelves.Count == 0)
            return new RecommendationHomeDto([], IsColdStart: true);

        var wanted = shelves.Where(shelf => shelf.ShelfKey != ShelfKeys.MixPool).ToList();

        var size = Math.Clamp(sectionSize, 1, RecommendationTuning.Shelves.ShelfSize);
        var sections = await hydrator.HydrateAsync(userId, wanted, size, includeScores, ct);

        var profile = await db.UserTasteProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        return new RecommendationHomeDto(
            sections,
            profile is null || profile.PositiveSignalCount == 0);
    }

    /// <summary>The pool the daily mix is drawn from, with scores, suppressed tracks removed.</summary>
    public async Task<IReadOnlyList<RecommendedTrackDto>> GetMixPoolAsync(CancellationToken ct = default)
    {
        var userId = currentUser.Id;
        var pool = (await LoadShelvesAsync(userId, ct))
            .Where(shelf => shelf.ShelfKey == ShelfKeys.MixPool)
            .ToList();

        if (pool.Count == 0)
            return [];

        var sections = await hydrator.HydrateAsync(
            userId, pool, RecommendationTuning.Shelves.MixPoolSize, includeScores: true, ct);

        return [.. sections.SelectMany(section => section.Tracks ?? [])];
    }

    /// <summary>
    /// Все полки одним плоским списком. Своего эндпоинта у этого чтения нет: единственный
    /// потребитель — <c>make eval</c>, который на этой выборке считает recall против базовой линии.
    /// Удалить как «никем не вызываемое» значит оставить оценку качества без предмета.
    /// </summary>
    public async Task<PagedResult<RecommendedTrackDto>> GetTracksAsync(
        PageRequest page, bool includeScores = false, CancellationToken ct = default)
    {
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

    private async Task<List<RecommendationCacheEntry>> LoadShelvesAsync(Guid userId, CancellationToken ct)
    {
        var cacheKey = RecommendationCacheKeys.Shelves(userId);

        if (memoryCache.TryGetValue(cacheKey, out List<RecommendationCacheEntry>? cached) && cached is not null)
            return cached;

        var shelves = await ReadShelvesAsync(userId, ct);

        if (shelves.Count == 0)
        {
            shelves = await BuildOnceAsync(userId, ct);
        }
        else
        {
            var now = clock.GetUtcNow();

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
        var gate = inlineBuilds.For(userId);

        await gate.WaitAsync(ct);
        try
        {
            var built = await ReadShelvesAsync(userId, ct);
            if (built.Count > 0)
                return built;

            return await GenerateInlineAsync(userId, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<List<RecommendationCacheEntry>> GenerateInlineAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            await generation.GenerateAsync(userId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Inline recommendation generation failed for user {UserId}", userId);
            return [];
        }

        return await ReadShelvesAsync(userId, ct);
    }

}
