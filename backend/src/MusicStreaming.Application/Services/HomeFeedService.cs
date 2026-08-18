using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
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

public class HomeFeedService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    CatalogService catalog,
    RecommendationService recommendations,
    StatisticsService statistics,
    UserSettingsService settings)
{
    private const int MinimumBlockSize = 4;
    private const int MinimumHeroSize = 5;
    private const int DailyMixSize = 20;
    private const int MosaicSize = 4;
    private const int QuickTileTracks = 6;
    private const int QuickTilePlaylists = 3;
    private const int MaxRecommendationShelves = 3;

    private static readonly string[] ShelfPriority =
    [
        ShelfKeys.ForYou,
        ShelfKeys.BecauseYouListened,
        ShelfKeys.Discover,
        ShelfKeys.GenreMix,
        ShelfKeys.SimilarTo,
        ShelfKeys.AlbumsForYou,
    ];

    public async Task<HomeFeedDto> GetAsync(int sectionSize, CancellationToken ct = default)
    {
        var summary = await catalog.GetHomeSummaryAsync(sectionSize, ct);

        if (summary.RecentlyAdded.Count == 0)
            return new HomeFeedDto([], summary.Stats, IsColdStart: true, GeneratedAt: null);

        var personal = await recommendations.GetHomeAsync(sectionSize, ct: ct);
        var top = await statistics.GetAsync(StatisticsPeriod.Week, ct);

        var shelves = PickShelves(personal.Sections);
        var artists = personal.Sections.FirstOrDefault(
            section => section.BaseKey == ShelfKeys.ArtistsForYou && Counted(section) >= MinimumBlockSize);

        var blocks = new List<HomeBlockDto?>
        {
            TrackBlock(HomeBlockKeys.DailyMix, HomeBlockLayout.Hero, await DailyMixAsync(personal, summary, ct), MinimumHeroSize),
            FavoritesTile(summary.Favorites, await FavoriteCountAsync(ct)),
            QuickTiles(summary.RecentlyPlayed, summary.Playlists),
            TrackBlock(HomeBlockKeys.NewArrivals, HomeBlockLayout.Grid, summary.RecentlyAdded, MinimumBlockSize),
            Recommendation(shelves.ElementAtOrDefault(0)),
            TrackBlock(HomeBlockKeys.TopTracks, HomeBlockLayout.Chart, [.. top.TopTracks.Select(entry => entry.Track)], MinimumBlockSize),
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

    private async Task<IReadOnlyList<TrackDto>> DailyMixAsync(
        RecommendationHomeDto personal, HomeSummaryDto summary, CancellationToken ct)
    {
        var known = new Dictionary<Guid, TrackDto>();
        var pool = new List<Guid>();

        foreach (var section in personal.Sections)
        {
            if (section.BaseKey is ShelfKeys.Popular or ShelfKeys.NewReleases or ShelfKeys.ContinueListening)
                continue;

            foreach (var item in section.Tracks ?? [])
                if (known.TryAdd(item.Track.Id, item.Track))
                    pool.Add(item.Track.Id);
        }

        if (pool.Count < MinimumHeroSize)
            foreach (var track in summary.Favorites.Concat(summary.RecentlyAdded))
                if (known.TryAdd(track.Id, track))
                    pool.Add(track.Id);

        if (pool.Count < MinimumHeroSize)
            return [];

        var localDate = await LocalDateAsync(ct);

        return [.. DailyMix.Pick(currentUser.Id, localDate, pool, DailyMixSize).Select(id => known[id])];
    }

    private async Task<DateOnly> LocalDateAsync(CancellationToken ct)
    {
        var timeZone = (await settings.GetAsync(ct)).TimeZone;

        var dates = await db.Database.SqlQuery<DateTime>(
            $"""
            SELECT (now() AT TIME ZONE {timeZone})::date AS "Value"
            """).ToListAsync(ct);

        return DateOnly.FromDateTime(dates[0]);
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
            section.Reason,
            section.Tracks?.Select(item => item.Track).ToList(),
            section.Albums,
            section.Artists,
            null,
            null);
    }

    private static HomeBlockDto? TrackBlock(
        string key, HomeBlockLayout layout, IReadOnlyList<TrackDto> tracks, int minimum) =>
        tracks.Count < minimum
            ? null
            : new HomeBlockDto(key, key, layout, null, tracks, null, null, null, null);

    private static HomeBlockDto? FavoritesTile(IReadOnlyList<TrackDto> favorites, int total)
    {
        if (total == 0)
            return null;

        return new HomeBlockDto(
            HomeBlockKeys.Favorites,
            HomeBlockKeys.Favorites,
            HomeBlockLayout.Tile,
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
            : new HomeBlockDto(key, key, HomeBlockLayout.Shelf, null, null, albums, null, null, null);

    private static HomeBlockDto? PlaylistBlock(string key, IReadOnlyList<PlaylistDto> playlists) =>
        playlists.Count == 0
            ? null
            : new HomeBlockDto(key, key, HomeBlockLayout.Shelf, null, null, null, null, playlists, null);
}
