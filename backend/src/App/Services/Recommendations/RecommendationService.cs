// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Abstractions;
using App.Dtos;
using App.Recommendations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Domain.Entities.Recommendations;

namespace App.Services.Recommendations;

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

    private async Task<List<RecommendationCacheEntry>> LoadShelvesAsync(Guid userId, CancellationToken ct)
    {
        var cacheKey = RecommendationCacheKeys.Shelves(userId);

        if (memoryCache.TryGetValue(cacheKey, out List<RecommendationCacheEntry>? cached) && cached is not null)
            return cached;

        var shelves = await ReadShelvesAsync(userId, ct);

        if (shelves.Count == 0)
        {
            var gate = inlineBuilds.For(userId);

            await gate.WaitAsync(ct);
            try
            {
                shelves = await ReadShelvesAsync(userId, ct);

                if (shelves.Count == 0)
                {
                    try
                    {
                        await generation.GenerateAsync(userId, ct);
                        shelves = await ReadShelvesAsync(userId, ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogError(ex, "Inline recommendation generation failed for user {UserId}", userId);
                    }
                }
            }
            finally
            {
                gate.Release();
            }
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
}
