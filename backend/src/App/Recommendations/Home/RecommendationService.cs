// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Infrastructure.Persistence;
using App.Abstractions;
using App.Common;
using App.Dtos;
using Microsoft.EntityFrameworkCore;
using Domain.Entities.Recommendations;

namespace App.Recommendations.Home;

public class RecommendationService(
    ApplicationDbContext db,
    ICurrentUser currentUser,
    ShelfGenerationService generation,
    RecommendationRefreshQueue refreshQueue,
    InlineBuildGate inlineBuilds,
    TimeProvider clock,
    ILogger<RecommendationService> logger)
{
    public async Task<IReadOnlyList<RecommendationSectionDto>> GetHomeAsync(int sectionSize, CancellationToken ct = default)
    {
        var userId = currentUser.Id;
        var shelves = await LoadShelvesAsync(userId, ct);

        if (shelves.Count == 0)
            return [];

        var wanted = shelves.Where(shelf => shelf.ShelfKey != ShelfKeys.MixPool).ToList();

        var size = Math.Clamp(sectionSize, 1, RecommendationTuning.Shelves.ShelfSize);
        return await HydrateAsync(userId, wanted, size, includeScores: false, ct);
    }

    public async Task<IReadOnlyList<RecommendedTrackDto>> GetMixPoolAsync(CancellationToken ct = default)
    {
        var userId = currentUser.Id;
        var pool = (await LoadShelvesAsync(userId, ct))
            .Where(shelf => shelf.ShelfKey == ShelfKeys.MixPool)
            .ToList();

        if (pool.Count == 0)
            return [];

        var sections = await HydrateAsync(
            userId, pool, RecommendationTuning.Shelves.MixPoolSize, includeScores: true, ct);

        return [.. sections.SelectMany(section => section.Tracks ?? [])];
    }

    private async Task<List<RecommendationCacheEntry>> LoadShelvesAsync(Guid userId, CancellationToken ct)
    {
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

        return shelves;
    }

    private async Task<List<RecommendationCacheEntry>> ReadShelvesAsync(Guid userId, CancellationToken ct) =>
        await db.RecommendationCache.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Position)
            .ToListAsync(ct);

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

        var artistIds = Ids(wanted, RecommendedItemKind.Artist).ToList();
        var artists = artistIds.Count == 0
            ? []
            : await db.Artists.AsNoTracking()
                .Where(a => artistIds.Contains(a.Id))
                .Select(ToDto.Artist)
                .ToDictionaryAsync(a => a.Id, ct);

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
                    [.. items.Where(item => artists.ContainsKey(item.ItemId)).Select(item => artists[item.ItemId])], null),

                _ => new RecommendationSectionDto(
                    shelf.ShelfKey, ShelfKeys.BaseOf(shelf.ShelfKey), reason,
                    items.Where(item => tracks.ContainsKey(item.ItemId))
                        .Select(item => new RecommendedTrackDto(
                            tracks[item.ItemId], ReasonOf(item), includeScores ? item.Score : null))
                        .ToList(),
                    null, null),
            };

            if (section.Tracks is { Count: > 0 } || section.Artists is { Count: > 0 } || section.Albums is { Count: > 0 })
                sections.Add(section);
        }

        return sections;
    }

    private static IEnumerable<Guid> Ids(
        List<(RecommendationCacheEntry Shelf, CachedRecommendation Item)> wanted, RecommendedItemKind kind) =>
        wanted.Where(w => w.Item.Kind == kind).Select(w => w.Item.ItemId).Distinct();

    private static RecommendationReasonDto ReasonOf(CachedRecommendation item) =>
        new(item.ReasonKind, item.ReasonSubject, item.ReasonSubjectId);
}
