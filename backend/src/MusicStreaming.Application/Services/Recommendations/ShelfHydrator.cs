// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

/// <summary>
/// Превращает кэшированные полки в DTO: догружает сущности по id, отсеивает подавленное и
/// засчитывает показы. Что именно отдавать, решает <see cref="RecommendationService"/>.
/// </summary>
public class ShelfHydrator(
    IApplicationDbContext db,
    TimeProvider clock,
    RecommendationMetrics metrics)
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
        var artists = await db.ArtistsByIdAsync(Ids(wanted, RecommendedItemKind.Artist), ct);
        var albums = await db.AlbumsByIdAsync(Ids(wanted, RecommendedItemKind.Album), ct);

        // Полки живут до шести часов, поэтому подавление применяется ещё и при отдаче: иначе
        // «не интересно» не давало бы видимого эффекта до следующей пересборки.
        var suppressed = await LoadSuppressionsAsync(userId, ct);
        tracks = tracks
            .Where(pair => !suppressed.Hides(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        artists = artists
            .Where(pair => !suppressed.Artists.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

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

        await RecordImpressionsAsync(userId, sections, ct);

        return sections;
    }

    /// <summary>
    /// Показ засчитывается при отдаче полок, а не при их сборке: перегенерация кэша ещё не значит,
    /// что человек это видел. Не чаще одного раза в сутки на (пользователь, трек, полка) — иначе
    /// каждое открытие главной душило бы весь пул кандидатов через UnclickedImpressionPenalty.
    /// </summary>
    private async Task RecordImpressionsAsync(
        Guid userId, List<RecommendationSectionDto> sections, CancellationToken ct)
    {
        var shown = sections
            .Where(section => section.Tracks is { Count: > 0 })
            .SelectMany(section => section.Tracks!.Select((track, position) =>
                (Shelf: section.Key, TrackId: track.Track.Id, Position: position)))
            .ToList();

        if (shown.Count == 0)
            return;

        var now = clock.GetUtcNow();
        var since = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var trackIds = shown.Select(item => item.TrackId).Distinct().ToList();

        var alreadyShown = (await db.RecommendationImpressions.AsNoTracking()
                .Where(i => i.UserId == userId && i.ShownAt >= since && trackIds.Contains(i.TrackId))
                .Select(i => new { i.TrackId, i.ShelfKey })
                .ToListAsync(ct))
            .Select(i => (i.ShelfKey, i.TrackId))
            .ToHashSet();

        var fresh = shown
            .Where(item => alreadyShown.Add((item.Shelf, item.TrackId)))
            .ToList();

        if (fresh.Count == 0)
            return;

        foreach (var item in fresh)
        {
            db.RecommendationImpressions.Add(new RecommendationImpression
            {
                UserId = userId,
                TrackId = item.TrackId,
                ShelfKey = item.Shelf,
                Position = item.Position,
                ShownAt = now,
            });
        }

        await db.SaveChangesAsync(ct);

        foreach (var group in fresh.GroupBy(item => ShelfKeys.BaseOf(item.Shelf)))
            metrics.RecordImpressions(group.Count(), group.Key);
    }

    private async Task<SuppressionSet> LoadSuppressionsAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        var rows = await db.RecommendationSuppressions.AsNoTracking()
            .Where(s => s.UserId == userId && (s.ExpiresAt == null || s.ExpiresAt > now))
            .Select(s => new { s.Target, s.TargetId })
            .ToListAsync(ct);

        return new SuppressionSet(
            rows.Where(r => r.Target == SuppressionTarget.Track).Select(r => r.TargetId).ToHashSet(),
            rows.Where(r => r.Target == SuppressionTarget.Artist).Select(r => r.TargetId).ToHashSet());
    }

    private sealed record SuppressionSet(HashSet<Guid> Tracks, HashSet<Guid> Artists)
    {
        public bool Hides(TrackDto track)
        {
            if (Tracks.Contains(track.Id) || Artists.Contains(track.ArtistId))
                return true;

            foreach (var artist in track.Artists)
            {
                if (Artists.Contains(artist.Id))
                    return true;
            }

            return false;
        }
    }

    private static IEnumerable<Guid> Ids(
        List<(RecommendationCacheEntry Shelf, CachedRecommendation Item)> wanted, RecommendedItemKind kind) =>
        wanted.Where(w => w.Item.Kind == kind).Select(w => w.Item.ItemId).Distinct();

    public static List<T> Resolve<T>(List<CachedRecommendation> items, Dictionary<Guid, T> loaded) =>
        items.Where(item => loaded.ContainsKey(item.ItemId)).Select(item => loaded[item.ItemId]).ToList();

    private static bool SectionIsEmpty(RecommendationSectionDto section) =>
        (section.Tracks?.Count ?? 0) == 0
        && (section.Artists?.Count ?? 0) == 0
        && (section.Albums?.Count ?? 0) == 0;

    public static RecommendedTrackDto ToDto(TrackDto track, CachedRecommendation item, bool includeScores) =>
        new(track, ReasonOf(item), includeScores ? item.Score : null);

    private static RecommendationReasonDto ReasonOf(CachedRecommendation item) =>
        new(item.ReasonKind, item.ReasonSubject, item.ReasonSubjectId);
}
