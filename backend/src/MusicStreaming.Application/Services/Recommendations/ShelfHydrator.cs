// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

public class ShelfHydrator(IApplicationDbContext db)
{
    public async Task<List<RecommendationSectionDto>> HydrateAsync(
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

        var albumIds = Ids(wanted, RecommendedItemKind.Album).ToList();
        var albums = albumIds.Count == 0
            ? []
            : await db.Albums.AsNoTracking()
                .Where(a => albumIds.Contains(a.Id))
                .Select(ToDto.Album)
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
                    Resolve(items, artists), null),

                RecommendedItemKind.Album => new RecommendationSectionDto(
                    shelf.ShelfKey, ShelfKeys.BaseOf(shelf.ShelfKey), reason, null, null,
                    Resolve(items, albums)),

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

    public static List<T> Resolve<T>(List<CachedRecommendation> items, Dictionary<Guid, T> loaded) =>
        items.Where(item => loaded.ContainsKey(item.ItemId)).Select(item => loaded[item.ItemId]).ToList();

    private static RecommendationReasonDto ReasonOf(CachedRecommendation item) =>
        new(item.ReasonKind, item.ReasonSubject, item.ReasonSubjectId);
}
