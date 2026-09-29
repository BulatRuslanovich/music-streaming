// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services.Recommendations;

namespace MusicStreaming.Application.Services;

/// <summary>
/// Собирает ленту главной: тянет материал из каталога, статистики и рекомендаций, а затем
/// раскладывает его по блокам. Какой блок из чего состоит — в <see cref="HomeBlocks"/>.
/// </summary>
public class HomeFeedService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    CatalogService catalog,
    LibraryOverviewService overview,
    DailyMixSnapshotStore dailyMix,
    RecommendationService recommendations,
    TimeProvider clock)
{
    private const int MixSize = 20;

    private static readonly TimeSpan TopWindow = TimeSpan.FromDays(7);

    public async Task<HomeFeedDto> GetAsync(int sectionSize, CancellationToken ct)
    {
        var summary = await overview.GetHomeSummaryAsync(sectionSize, ct);

        if (summary.RecentlyAdded.Count == 0)
            return new HomeFeedDto([], summary.Stats, IsColdStart: true);

        var personal = await recommendations.GetHomeAsync(sectionSize, ct: ct);
        var top = await TopTracksAsync(sectionSize, ct);

        var shelves = HomeBlocks.PickShelves(personal.Sections);
        var artists = personal.Sections.FirstOrDefault(
            section => section.BaseKey == ShelfKeys.ArtistsForYou
                       && HomeBlocks.Counted(section) >= HomeBlocks.MinimumBlockSize);

        // Порядок зоны Browse чередует макеты, а не темы: подряд идущие Shelf-блоки — это
        // четыре одинаковые ленты 11rem-карточек с одинаковой шапкой, и страница читается
        // как один список. Сетка новинок, чарт и круги исполнителей растащены между полками,
        // так что стык двух Shelf остаётся ровно один — в самом низу, ниже сгиба на любом
        // экране. Развести четыре полки тремя не-полками полностью нельзя.
        //
        // Третьей рекомендательной полки здесь нет намеренно: PickShelves режет список по
        // MaxRecommendationShelves = 2, и ElementAtOrDefault(2) возвращал null всегда.
        var blocks = new List<HomeBlockDto?>
        {
            HomeBlocks.Hero(await dailyMix.TodayAsync(ct)),
            HomeBlocks.FavoritesTile(
                summary.Favorites,
                summary.Favorites.Count < sectionSize
                    ? summary.Favorites.Count
                    : await FavoriteCountAsync(ct)),
            HomeBlocks.QuickTiles(summary.RecentlyPlayed, summary.Playlists),
            HomeBlocks.TrackBlock(
                HomeBlockKeys.NewArrivals,
                HomeBlockLayout.Grid,
                HomeZone.Browse,
                summary.RecentlyAdded,
                HomeBlocks.MinimumBlockSize),
            HomeBlocks.Recommendation(shelves.ElementAtOrDefault(0)),
            HomeBlocks.TrackBlock(
                HomeBlockKeys.TopTracks,
                HomeBlockLayout.Chart,
                HomeZone.Browse,
                top,
                HomeBlocks.MinimumBlockSize),
            HomeBlocks.AlbumBlock(HomeBlockKeys.NewAlbums, summary.Albums),
            HomeBlocks.Recommendation(artists),
            HomeBlocks.Recommendation(shelves.ElementAtOrDefault(1)),
            HomeBlocks.PlaylistBlock(HomeBlockKeys.YourPlaylists, summary.Playlists),
        };

        return new HomeFeedDto(
            [.. blocks.OfType<HomeBlockDto>()],
            summary.Stats,
            personal.IsColdStart);
    }

    public async Task<HomeMixDto> GetMixAsync(HomeMixKind kind, CancellationToken ct)
    {
        IReadOnlyList<TrackDto> tracks = kind switch
        {
            HomeMixKind.New => (await catalog.GetTracksAsync(
                new PageRequest(1, MixSize), CatalogService.TrackSort.Recent, null, ct: ct)).Items,

            HomeMixKind.Top => await TopTracksAsync(MixSize, ct),

            _ => await dailyMix.TodayAsync(ct),
        };

        return new HomeMixDto(kind, tracks);
    }

    /// <summary>
    /// The listener's most played tracks of the last seven days, by listening time.
    /// </summary>
    /// <remarks>
    /// Окно скользящее, а не календарная неделя в поясе слушателя: для чарта на главной
    /// разница в несколько часов незаметна, а граница дня по поясу тянула за собой SQL с
    /// AT TIME ZONE и чтение настроек.
    /// </remarks>
    private async Task<IReadOnlyList<TrackDto>> TopTracksAsync(int size, CancellationToken ct)
    {
        var from = clock.GetUtcNow() - TopWindow;

        var top = await db.ListeningStats
            .AsNoTracking()
            .Where(stat => stat.UserId == currentUser.Id && stat.Hour >= from)
            .GroupBy(stat => stat.TrackId)
            .Select(group => new
            {
                TrackId = group.Key,
                ListenedSeconds = group.Sum(stat => stat.ListenedSeconds),
                Plays = group.Sum(stat => stat.PlayCount),
            })
            .OrderByDescending(entry => entry.ListenedSeconds)
            .ThenByDescending(entry => entry.Plays)
            .Take(size)
            .ToListAsync(ct);

        var tracks = await db.TracksByIdAsync(currentUser.Id, top.Select(entry => entry.TrackId), ct);

        return [.. top.Where(entry => tracks.ContainsKey(entry.TrackId)).Select(entry => tracks[entry.TrackId])];
    }

    private Task<int> FavoriteCountAsync(CancellationToken ct) =>
        db.Favorites.AsNoTracking().CountAsync(favorite => favorite.UserId == currentUser.Id, ct);
}
