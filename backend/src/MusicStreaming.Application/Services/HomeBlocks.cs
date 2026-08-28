// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

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
/// Чистые фабрики блоков ленты: что показать и каким макетом. Возвращают <c>null</c>, когда
/// материала не набралось — половина ленты выпадает именно так, и решает это блок, а не сборщик.
/// </summary>
public static class HomeBlocks
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
        ShelfKeys.GenreMix,
        ShelfKeys.SimilarTo,
        ShelfKeys.AlbumsForYou,
    ];

    public static IReadOnlyList<RecommendationSectionDto> PickShelves(
        IReadOnlyList<RecommendationSectionDto> sections)
    {
        var picked = new List<RecommendationSectionDto>();

        foreach (var baseKey in ShelfPriority)
        {
            if (picked.Count == MaxRecommendationShelves)
                break;

            var section = sections.FirstOrDefault(
                candidate => candidate.BaseKey == baseKey && Counted(candidate) >= MinimumBlockSize);

            if (section is not null)
                picked.Add(section);
        }

        return picked;
    }

    public static int Counted(RecommendationSectionDto section) =>
        section.Tracks?.Count ?? section.Artists?.Count ?? section.Albums?.Count ?? 0;

    public static HomeBlockDto? Recommendation(RecommendationSectionDto? section)
    {
        if (section is null)
            return null;

        var layout = section.BaseKey == ShelfKeys.ArtistsForYou
            ? HomeBlockLayout.Circles
            : HomeBlockLayout.Shelf;

        return new HomeBlockDto(
            section.Key,
            section.BaseKey,
            layout,
            HomeZone.Browse,
            section.Reason,
            section.Tracks?.Select(item => item.Track).ToList(),
            section.Albums,
            section.Artists,
            null,
            null);
    }

    /// <summary>
    /// Геро-блок отдаёт превью микса дня и его полный размер отдельным числом — как плитка
    /// избранного отдаёт четыре обложки и общий счёт.
    /// </summary>
    public static HomeBlockDto? Hero(IReadOnlyList<TrackDto> mix) =>
        mix.Count < MinimumHeroSize
            ? null
            : new HomeBlockDto(
                HomeBlockKeys.DailyMix,
                HomeBlockKeys.DailyMix,
                HomeBlockLayout.Hero,
                HomeZone.Lead,
                null,
                [.. mix.Take(HeroTracks)],
                null,
                null,
                null,
                mix.Count);

    public static HomeBlockDto? TrackBlock(
        string key, HomeBlockLayout layout, HomeZone zone, IReadOnlyList<TrackDto> tracks, int minimum) =>
        tracks.Count < minimum
            ? null
            : new HomeBlockDto(key, key, layout, zone, null, tracks, null, null, null, null);

    public static HomeBlockDto? FavoritesTile(IReadOnlyList<TrackDto> favorites, int total)
    {
        if (total == 0)
            return null;

        return new HomeBlockDto(
            HomeBlockKeys.Favorites,
            HomeBlockKeys.Favorites,
            HomeBlockLayout.Tile,
            HomeZone.Quick,
            null,
            [.. favorites.Take(MosaicSize)],
            null,
            null,
            null,
            total);
    }

    public static HomeBlockDto? QuickTiles(
        IReadOnlyList<TrackDto> recentlyPlayed, IReadOnlyList<PlaylistDto> playlists)
    {
        var tracks = recentlyPlayed.Take(QuickTileTracks).ToList();
        var recent = playlists.Take(QuickTilePlaylists).ToList();

        if (tracks.Count + recent.Count == 0)
            return null;

        return new HomeBlockDto(
            HomeBlockKeys.QuickTiles,
            HomeBlockKeys.QuickTiles,
            HomeBlockLayout.QuickTiles,
            HomeZone.Quick,
            null,
            tracks,
            null,
            null,
            recent,
            null);
    }

    public static HomeBlockDto? AlbumBlock(string key, IReadOnlyList<AlbumDto> albums) =>
        albums.Count < MinimumBlockSize
            ? null
            : new HomeBlockDto(
                key, key, HomeBlockLayout.Shelf, HomeZone.Browse, null, null, albums, null, null, null);

    public static HomeBlockDto? PlaylistBlock(string key, IReadOnlyList<PlaylistDto> playlists) =>
        playlists.Count == 0
            ? null
            : new HomeBlockDto(
                key, key, HomeBlockLayout.Shelf, HomeZone.Browse, null, null, null, null, playlists, null);
}
