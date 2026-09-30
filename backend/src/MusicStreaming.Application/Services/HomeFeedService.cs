// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services.Recommendations;

namespace MusicStreaming.Application.Services;

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

/// <summary>
/// Собирает ленту главной: тянет материал из каталога, статистики и рекомендаций и раскладывает
/// его по блокам. Блок, для которого материала не набралось, выпадает из ленты — половина ленты
/// выпадает именно так.
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
    public const int MinimumBlockSize = 4;
    public const int MinimumHeroSize = 5;

    /// <summary>
    /// Сколько треков микса дня уезжает в ленту. Spotlight показывает четыре, но кнопка «играть»
    /// ставит в очередь весь блок, и двадцати хватает дольше любого сеанса на главной — а дальше
    /// очередь и так дотягивается радио. Полный микс живёт за <c>/api/home/mixes/daily</c>.
    /// </summary>
    public const int HeroTracks = 20;

    private const int MosaicSize = 4;
    private const int QuickTileTracks = 5;
    private const int QuickTilePlaylists = 2;
    private const int MixSize = 20;

    /// <summary>
    /// Три однотипные полки «для вас» спорили друг с другом и с геро-миксом, собранным из того же
    /// пула. Приоритет полок задан в <see cref="ShelfPriority"/>, так что режется наименее важная.
    /// </summary>
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
            return new HomeFeedDto([], summary.Stats, IsColdStart: true);

        var personal = await recommendations.GetHomeAsync(sectionSize, ct: ct);
        var top = await TopTracksAsync(sectionSize, ct);
        var mix = await dailyMix.TodayAsync(ct);

        var shelves = ShelfPriority
            .Select(baseKey => personal.Sections.FirstOrDefault(
                section => section.BaseKey == baseKey && Counted(section) >= MinimumBlockSize))
            .OfType<RecommendationSectionDto>()
            .Take(MaxRecommendationShelves)
            .ToList();

        var artists = personal.Sections.FirstOrDefault(
            section => section.BaseKey == ShelfKeys.ArtistsForYou && Counted(section) >= MinimumBlockSize);

        var favoriteCount = summary.Favorites.Count < sectionSize
            ? summary.Favorites.Count
            : await db.Favorites.AsNoTracking().CountAsync(favorite => favorite.UserId == currentUser.Id, ct);

        var quickTracks = summary.RecentlyPlayed.Take(QuickTileTracks).ToList();
        var quickPlaylists = summary.Playlists.Take(QuickTilePlaylists).ToList();

        // Порядок зоны Browse чередует макеты, а не темы: подряд идущие Shelf-блоки — это
        // четыре одинаковые ленты 11rem-карточек с одинаковой шапкой, и страница читается
        // как один список. Сетка новинок, чарт и круги исполнителей растащены между полками,
        // так что стык двух Shelf остаётся ровно один — в самом низу, ниже сгиба на любом
        // экране. Развести четыре полки тремя не-полками полностью нельзя.
        //
        // Геро-блок и плитка избранного отдают превью и полный размер отдельным числом.
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
            quickTracks.Count + quickPlaylists.Count == 0
                ? null
                : new HomeBlockDto(
                    HomeBlockKeys.QuickTiles, HomeBlockKeys.QuickTiles, HomeBlockLayout.QuickTiles, HomeZone.Quick,
                    Tracks: quickTracks, Playlists: quickPlaylists),
            TrackBlock(HomeBlockKeys.NewArrivals, HomeBlockLayout.Grid, summary.RecentlyAdded),
            Recommendation(shelves.ElementAtOrDefault(0)),
            TrackBlock(HomeBlockKeys.TopTracks, HomeBlockLayout.Chart, top),
            summary.Albums.Count < MinimumBlockSize
                ? null
                : new HomeBlockDto(
                    HomeBlockKeys.NewAlbums, HomeBlockKeys.NewAlbums, HomeBlockLayout.Shelf, HomeZone.Browse,
                    Albums: summary.Albums),
            Recommendation(artists),
            Recommendation(shelves.ElementAtOrDefault(1)),
            summary.Playlists.Count == 0
                ? null
                : new HomeBlockDto(
                    HomeBlockKeys.YourPlaylists, HomeBlockKeys.YourPlaylists, HomeBlockLayout.Shelf, HomeZone.Browse,
                    Playlists: summary.Playlists),
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
