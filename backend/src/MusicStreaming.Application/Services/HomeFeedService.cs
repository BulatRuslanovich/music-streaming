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
    StatisticsService statistics)
{
    private const int MixSize = 20;

    public async Task<HomeFeedDto> GetAsync(int sectionSize, CancellationToken ct)
    {
        var summary = await overview.GetHomeSummaryAsync(sectionSize, ct);

        if (summary.RecentlyAdded.Count == 0)
            return new HomeFeedDto([], summary.Stats, IsColdStart: true, GeneratedAt: null);

        var personal = await recommendations.GetHomeAsync(sectionSize, ct: ct);
        var top = await statistics.TopTracksAsync(StatisticsPeriod.Week, sectionSize, ct);

        var shelves = HomeBlocks.PickShelves(personal.Sections);
        var artists = personal.Sections.FirstOrDefault(
            section => section.BaseKey == ShelfKeys.ArtistsForYou
                       && HomeBlocks.Counted(section) >= HomeBlocks.MinimumBlockSize);

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
                [.. top.Select(entry => entry.Track)],
                HomeBlocks.MinimumBlockSize),
            HomeBlocks.Recommendation(shelves.ElementAtOrDefault(1)),
            HomeBlocks.AlbumBlock(HomeBlockKeys.NewAlbums, summary.Albums),
            HomeBlocks.Recommendation(artists),
            HomeBlocks.Recommendation(shelves.ElementAtOrDefault(2)),
            HomeBlocks.PlaylistBlock(HomeBlockKeys.YourPlaylists, summary.Playlists),
        };

        return new HomeFeedDto(
            [.. blocks.OfType<HomeBlockDto>()],
            summary.Stats,
            personal.IsColdStart,
            personal.GeneratedAt);
    }

    public async Task<HomeMixDto> GetMixAsync(HomeMixKind kind, CancellationToken ct)
    {
        IReadOnlyList<TrackDto> tracks = kind switch
        {
            HomeMixKind.New => (await catalog.GetTracksAsync(
                new PageRequest(1, MixSize), CatalogService.TrackSort.Recent, null, ct: ct)).Items,

            HomeMixKind.Top => [.. (await statistics.TopTracksAsync(StatisticsPeriod.Week, MixSize, ct))
                .Select(entry => entry.Track)],

            _ => await dailyMix.TodayAsync(ct),
        };

        return new HomeMixDto(kind, tracks);
    }

    private Task<int> FavoriteCountAsync(CancellationToken ct) =>
        db.Favorites.AsNoTracking().CountAsync(favorite => favorite.UserId == currentUser.Id, ct);
}
