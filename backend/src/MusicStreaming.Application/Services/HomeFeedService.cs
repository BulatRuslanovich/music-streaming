// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services.Recommendations;
using MusicStreaming.Domain.Entities.Recommendations;

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

public class HomeFeedService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    CatalogService catalog,
    RecommendationService recommendations,
    StatisticsService statistics,
    UserSettingsService settings,
    TimeProvider clock)
{
    private const int MinimumBlockSize = 4;
    private const int MinimumHeroSize = 5;
    private const int MixSize = 20;

    /// <summary>Размер плейлиста дня: он собирается раз в сутки, поэтому его хватает на весь день.</summary>
    private const int DailyMixSize = 60;
    private const int MosaicSize = 4;

    private const int QuickTileTracks = 5;
    private const int QuickTilePlaylists = 2;
    private const int MaxRecommendationShelves = 3;

    /// <summary>Вес трека, попавшего в пул без скора (избранное и свежие поступления на подхвате).</summary>
    private const double FallbackWeight = 0.15;

    private static readonly string[] ShelfPriority =
    [
        ShelfKeys.ForYou,
        ShelfKeys.BecauseYouListened,
        ShelfKeys.Discover,
        ShelfKeys.GenreMix,
        ShelfKeys.SimilarTo,
        ShelfKeys.AlbumsForYou,
    ];

    public async Task<HomeFeedDto> GetAsync(int sectionSize, CancellationToken ct)
    {
        var summary = await catalog.GetHomeSummaryAsync(sectionSize, ct);

        if (summary.RecentlyAdded.Count == 0)
            return new HomeFeedDto([], summary.Stats, IsColdStart: true, GeneratedAt: null);

        var personal = await recommendations.GetHomeAsync(sectionSize, ct: ct);
        var top = await statistics.TopTracksAsync(StatisticsPeriod.Week, sectionSize, ct);

        var shelves = PickShelves(personal.Sections);
        var artists = personal.Sections.FirstOrDefault(
            section => section.BaseKey == ShelfKeys.ArtistsForYou && Counted(section) >= MinimumBlockSize);

        var blocks = new List<HomeBlockDto?>
        {
            TrackBlock(
                HomeBlockKeys.DailyMix,
                HomeBlockLayout.Hero,
                HomeZone.Lead,
                await DailyMixAsync(ct),
                MinimumHeroSize),
            FavoritesTile(
                summary.Favorites,
                summary.Favorites.Count < sectionSize
                    ? summary.Favorites.Count
                    : await FavoriteCountAsync(ct)),
            QuickTiles(summary.RecentlyPlayed, summary.Playlists),
            TrackBlock(
                HomeBlockKeys.NewArrivals,
                HomeBlockLayout.Grid,
                HomeZone.Browse,
                summary.RecentlyAdded,
                MinimumBlockSize),
            Recommendation(shelves.ElementAtOrDefault(0)),
            TrackBlock(
                HomeBlockKeys.TopTracks,
                HomeBlockLayout.Chart,
                HomeZone.Browse,
                [.. top.Select(entry => entry.Track)],
                MinimumBlockSize),
            Recommendation(shelves.ElementAtOrDefault(1)),
            AlbumBlock(HomeBlockKeys.NewAlbums, summary.Albums),
            Recommendation(artists),
            Recommendation(shelves.ElementAtOrDefault(2)),
            PlaylistBlock(HomeBlockKeys.YourPlaylists, summary.Playlists),
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

            _ => await DailyMixAsync(ct),
        };

        return new HomeMixDto(kind, tracks);
    }

    /// <summary>
    /// Плейлист дня фиксируется на локальную дату слушателя: первый заход за день собирает микс и
    /// запоминает его, все остальные читают тот же снимок. Пул под ним живёт своей жизнью —
    /// рекомендации пересчитываются после каждой сессии, а витрины ещё и меняются по времени
    /// суток, — так что без снимка «подборка на сегодня» переписывалась бы по нескольку раз в день.
    /// </summary>
    private async Task<IReadOnlyList<TrackDto>> DailyMixAsync(CancellationToken ct)
    {
        var userId = currentUser.Id;
        var localDate = await LocalDateAsync(ct);

        var stored = await db.DailyMixes.AsNoTracking()
            .FirstOrDefaultAsync(mix => mix.UserId == userId && mix.LocalDate == localDate, ct);

        var trackIds = stored?.TrackIds ?? await BuildDailyMixAsync(userId, localDate, ct);
        if (trackIds.Count == 0)
            return [];

        // Треки могли удалить уже после того, как микс был собран.
        var known = await db.TracksByIdAsync(userId, trackIds, ct);

        return [.. trackIds.Where(known.ContainsKey).Select(id => known[id])];
    }

    private async Task<IReadOnlyList<Guid>> BuildDailyMixAsync(
        Guid userId, DateOnly localDate, CancellationToken ct)
    {
        // Скоры нужны только для взвешивания микса и наружу не отдаются.
        var personal = await recommendations.GetHomeAsync(DailyMixSize, includeScores: true, ct: ct);

        var seen = new HashSet<Guid>();
        var pool = new List<(Guid Id, double Weight)>();

        foreach (var section in personal.Sections)
        {
            if (section.BaseKey is ShelfKeys.Popular or ShelfKeys.NewReleases or ShelfKeys.ContinueListening)
                continue;

            foreach (var item in section.Tracks ?? [])
                if (seen.Add(item.Track.Id))
                    pool.Add((item.Track.Id, item.Score ?? FallbackWeight));
        }

        if (pool.Count < DailyMixSize)
        {
            var summary = await catalog.GetHomeSummaryAsync(DailyMixSize, ct);

            foreach (var track in summary.Favorites.Concat(summary.RecentlyAdded))
                if (seen.Add(track.Id))
                    pool.Add((track.Id, FallbackWeight));
        }

        if (pool.Count < MinimumHeroSize)
            return [];

        var picked = DailyMix.PickWeighted(userId, localDate, pool, DailyMixSize);

        return await StoreDailyMixAsync(userId, localDate, picked, ct);
    }

    private async Task<IReadOnlyList<Guid>> StoreDailyMixAsync(
        Guid userId, DateOnly localDate, IReadOnlyList<Guid> trackIds, CancellationToken ct)
    {
        db.DailyMixes.Add(new DailyMixSnapshot
        {
            UserId = userId,
            LocalDate = localDate,
            TrackIds = trackIds,
            GeneratedAt = clock.GetUtcNow(),
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Параллельный запрос успел записать снимок на этот день — он и остаётся сегодняшним.
            db.ChangeTracker.Clear();

            var stored = await db.DailyMixes.AsNoTracking()
                .FirstOrDefaultAsync(mix => mix.UserId == userId && mix.LocalDate == localDate, ct);

            return stored?.TrackIds ?? trackIds;
        }

        await db.DailyMixes
            .Where(mix => mix.UserId == userId && mix.LocalDate < localDate)
            .ExecuteDeleteAsync(ct);

        return trackIds;
    }

    private async Task<DateOnly> LocalDateAsync(CancellationToken ct)
    {
        var timeZone = (await settings.GetAsync(ct)).TimeZone;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone);

        return DateOnly.FromDateTime(local.DateTime);
    }

    private Task<int> FavoriteCountAsync(CancellationToken ct) =>
        db.Favorites.AsNoTracking().CountAsync(favorite => favorite.UserId == currentUser.Id, ct);

    private static IReadOnlyList<RecommendationSectionDto> PickShelves(
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

    private static int Counted(RecommendationSectionDto section) =>
        section.Tracks?.Count ?? section.Artists?.Count ?? section.Albums?.Count ?? 0;

    private static HomeBlockDto? Recommendation(RecommendationSectionDto? section)
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

    private static HomeBlockDto? TrackBlock(
        string key, HomeBlockLayout layout, HomeZone zone, IReadOnlyList<TrackDto> tracks, int minimum) =>
        tracks.Count < minimum
            ? null
            : new HomeBlockDto(key, key, layout, zone, null, tracks, null, null, null, null);

    private static HomeBlockDto? FavoritesTile(IReadOnlyList<TrackDto> favorites, int total)
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

    private static HomeBlockDto? QuickTiles(
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

    private static HomeBlockDto? AlbumBlock(string key, IReadOnlyList<AlbumDto> albums) =>
        albums.Count < MinimumBlockSize
            ? null
            : new HomeBlockDto(
                key, key, HomeBlockLayout.Shelf, HomeZone.Browse, null, null, albums, null, null, null);

    private static HomeBlockDto? PlaylistBlock(string key, IReadOnlyList<PlaylistDto> playlists) =>
        playlists.Count == 0
            ? null
            : new HomeBlockDto(
                key, key, HomeBlockLayout.Shelf, HomeZone.Browse, null, null, null, null, playlists, null);
}
