// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Infrastructure.Persistence;
using App.Abstractions;
using App.Common;
using App.Dtos;
using Microsoft.EntityFrameworkCore;
using Domain.Entities.Recommendations;
using App.Recommendations.Home;

namespace App.Services;

public static class HomeBlockKeys
{
    public const string DailyMix = "dailyMix";
    public const string Favorites = "favorites";
    public const string QuickTiles = "quickTiles";
    public const string NewArrivals = "newArrivals";
    public const string TopTracks = "topTracks";
    public const string NewAlbums = "newAlbums";
    public const string YourPlaylists = "yourPlaylists";
}

public class HomeFeedService(
    ApplicationDbContext db,
    ICurrentUser currentUser,
    CatalogService catalog,
    LibraryOverviewService overview,
    DailyMixSnapshotStore dailyMix,
    RecommendationService recommendations,
    TimeProvider clock)
{
    public const int MinimumBlockSize = 4;
    public const int MinimumHeroSize = 5;

    public const int HeroTracks = 20;

    private const int MosaicSize = 4;
    private const int QuickTilePlaces = 5;
    private const int QuickTilePlaylists = 2;
    private const int MixSize = 20;

    private const int MaxRecommendationShelves = 2;

    private static readonly string[] ShelfPriority =
    [
        ShelfKeys.ForYou,
        ShelfKeys.BecauseYouListened,
        ShelfKeys.Discover,
    ];

    private static readonly TimeSpan TopWindow = TimeSpan.FromDays(7);

    public async Task<HomeFeedDto> GetAsync(int sectionSize, CancellationToken ct)
    {
        var summary = await overview.GetHomeSummaryAsync(sectionSize, ct);

        if (summary.RecentlyAdded.Count == 0)
            return new HomeFeedDto([], summary.Stats);

        var personal = await recommendations.GetHomeAsync(sectionSize, ct: ct);
        var top = await TopTracksAsync(sectionSize, ct);
        var mix = await dailyMix.TodayAsync(ct);

        var shelves = ShelfPriority
            .Select(baseKey => personal.FirstOrDefault(
                section => section.BaseKey == baseKey && Counted(section) >= MinimumBlockSize))
            .OfType<RecommendationSectionDto>()
            .Take(MaxRecommendationShelves)
            .ToList();

        var artists = personal.FirstOrDefault(
            section => section.BaseKey == ShelfKeys.ArtistsForYou && Counted(section) >= MinimumBlockSize);

        var favoriteCount = summary.Favorites.Count < sectionSize
            ? summary.Favorites.Count
            : await db.Favorites.AsNoTracking().CountAsync(favorite => favorite.UserId == currentUser.Id, ct);

        var quickPlaces = summary.RecentlyPlayed.DistinctBy(track => track.AlbumId ?? track.Id).Take(QuickTilePlaces).ToList();
        var quickAlbumIds = quickPlaces.Select(track => track.AlbumId).OfType<Guid>().ToList();
        var quickAlbums = await db.Albums
            .AsNoTracking()
            .Where(album => quickAlbumIds.Contains(album.Id))
            .Select(ToDto.Album)
            .ToDictionaryAsync(album => album.Id, ct);
        var quickPlaylists = summary.Playlists.Take(QuickTilePlaylists).ToList();

        var blocks = new List<HomeBlockDto?>
        {
            mix.Count < MinimumHeroSize
                ? null
                : new HomeBlockDto(
                    HomeBlockKeys.DailyMix, HomeBlockKeys.DailyMix, HomeBlockLayout.Hero, HomeZone.Lead,
                    Tracks: [.. mix.Take(HeroTracks)], TotalCount: mix.Count),
            favoriteCount == 0
                ? null
                : new HomeBlockDto(
                    HomeBlockKeys.Favorites, HomeBlockKeys.Favorites, HomeBlockLayout.Tile, HomeZone.Quick,
                    Tracks: [.. summary.Favorites.Take(MosaicSize)], TotalCount: favoriteCount),
            quickPlaces.Count + quickPlaylists.Count == 0
                ? null
                : new HomeBlockDto(
                    HomeBlockKeys.QuickTiles, HomeBlockKeys.QuickTiles, HomeBlockLayout.QuickTiles, HomeZone.Quick,
                    Tracks: [.. quickPlaces.Where(track => track.AlbumId is null)],
                    Albums: [.. quickAlbumIds.Where(quickAlbums.ContainsKey).Select(id => quickAlbums[id])],
                    Playlists: quickPlaylists),
            // Полки с обложками чередуются со списками треков, чтобы главная не превращалась в стену строк.
            summary.Albums.Count < MinimumBlockSize
                ? null
                : new HomeBlockDto(
                    HomeBlockKeys.NewAlbums, HomeBlockKeys.NewAlbums, HomeBlockLayout.Shelf, HomeZone.Browse,
                    Albums: summary.Albums),
            TrackBlock(HomeBlockKeys.NewArrivals, HomeBlockLayout.Grid, summary.RecentlyAdded),
            Recommendation(artists),
            Recommendation(shelves.ElementAtOrDefault(0)),
            summary.Playlists.Count == 0
                ? null
                : new HomeBlockDto(
                    HomeBlockKeys.YourPlaylists, HomeBlockKeys.YourPlaylists, HomeBlockLayout.Shelf, HomeZone.Browse,
                    Playlists: summary.Playlists),
            TrackBlock(HomeBlockKeys.TopTracks, HomeBlockLayout.Chart, top),
            Recommendation(shelves.ElementAtOrDefault(1)),
        };

        return new HomeFeedDto([.. blocks.OfType<HomeBlockDto>()], summary.Stats);
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

    private async Task<IReadOnlyList<TrackDto>> TopTracksAsync(int size, CancellationToken ct)
    {
        var from = clock.GetUtcNow() - TopWindow;

        // Прослушиванием считается законченная попытка (дослушал или переключил) длиннее порога истории;
        // промежуточные heartbeat-события не учитываются, чтобы не считать одно прослушивание дважды.
        var top = await db.PlaybackEvents
            .AsNoTracking()
            .Where(e => e.UserId == currentUser.Id
                        && e.TrackId != null
                        && e.OccurredAt >= from
                        && (e.Type == PlaybackEventType.TrackCompleted || e.Type == PlaybackEventType.TrackSkipped)
                        && e.ListenedSeconds >= HistoryService.ThresholdSeconds)
            .GroupBy(e => e.TrackId!.Value)
            .Select(group => new
            {
                TrackId = group.Key,
                ListenedSeconds = group.Sum(e => (long)e.ListenedSeconds),
                Plays = group.Count(),
            })
            .OrderByDescending(entry => entry.ListenedSeconds)
            .ThenByDescending(entry => entry.Plays)
            .Take(size)
            .ToListAsync(ct);

        var tracks = await db.TracksByIdAsync(currentUser.Id, top.Select(entry => entry.TrackId), ct);

        return [.. top.Where(entry => tracks.ContainsKey(entry.TrackId)).Select(entry => tracks[entry.TrackId])];
    }

    private static int Counted(RecommendationSectionDto section) =>
        section.Tracks?.Count ?? section.Artists?.Count ?? section.Albums?.Count ?? 0;

    private static HomeBlockDto? TrackBlock(string key, HomeBlockLayout layout, IReadOnlyList<TrackDto> tracks) =>
        tracks.Count < MinimumBlockSize ? null : new HomeBlockDto(key, key, layout, HomeZone.Browse, Tracks: tracks);

    private static HomeBlockDto? Recommendation(RecommendationSectionDto? section) =>
        section is null
            ? null
            : new HomeBlockDto(
                section.Key,
                section.BaseKey,
                section.BaseKey == ShelfKeys.ArtistsForYou ? HomeBlockLayout.Circles : HomeBlockLayout.Shelf,
                HomeZone.Browse,
                section.Reason,
                section.Tracks?.Select(item => item.Track).ToList(),
                section.Albums,
                section.Artists);
}
