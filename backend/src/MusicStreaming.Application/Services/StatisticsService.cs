using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

public class StatisticsService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    UserSettingsService settings)
{
    private const int TopSize = 10;

    public async Task<StatisticsDto> GetAsync(
        StatisticsPeriod period, CancellationToken ct = default)
    {
        var timeZone = (await settings.GetAsync(ct)).TimeZone;
        var from = await ResolveStartAsync(period, timeZone, ct);

        var scope = db.ListeningStats.AsNoTracking().Where(s => s.UserId == currentUser.Id);
        if (from is { } start)
            scope = scope.Where(s => s.Hour >= start);

        var byDay = await ByDayAsync(from, timeZone, ct);
        var byHour = await ByHourAsync(from, timeZone, ct);

        return new StatisticsDto(
            period,
            from,
            timeZone,
            await SummariseAsync(scope, byDay, byHour, ct),
            await TopTracksAsync(scope, ct),
            await TopArtistsAsync(scope, ct),
            await TopAlbumsAsync(scope, ct),
            await TopGenresAsync(scope, ct),
            byDay,
            byHour);
    }

    private async Task<DateTimeOffset?> ResolveStartAsync(
        StatisticsPeriod period, string timeZone, CancellationToken ct)
    {
        if (period == StatisticsPeriod.All)
            return null;

        var days = period switch
        {
            StatisticsPeriod.Week => 7,
            StatisticsPeriod.Month => 30,
            StatisticsPeriod.Quarter => 90,
            _ => 0,
        };

        var starts = await db.Database.SqlQuery<DateTime>(
            $"""
            SELECT (CASE
                        WHEN {days}::int = 0
                            THEN date_trunc('year', now() AT TIME ZONE {timeZone})
                        ELSE date_trunc('day', now() AT TIME ZONE {timeZone})
                             - make_interval(days => {days}::int - 1)
                    END AT TIME ZONE {timeZone}) AS "Value"
            """).ToListAsync(ct);

        return new DateTimeOffset(DateTime.SpecifyKind(starts[0], DateTimeKind.Utc));
    }

    private async Task<StatisticsSummaryDto> SummariseAsync(
        IQueryable<ListeningStat> scope,
        IReadOnlyList<DailyActivityDto> byDay,
        IReadOnlyList<HourlyActivityDto> byHour,
        CancellationToken ct)
    {
        var listenedSeconds = await scope.SumAsync(s => s.ListenedSeconds, ct);
        var plays = await scope.SumAsync(s => s.PlayCount, ct);

        var uniqueTracks = await scope.Select(s => s.TrackId).Distinct().CountAsync(ct);

        var uniqueAlbums = await scope
            .Where(s => s.Track!.AlbumId != null)
            .Select(s => s.Track!.AlbumId)
            .Distinct()
            .CountAsync(ct);

        var uniqueArtists = await db.TrackArtists
            .Where(credit => scope.Select(stat => stat.TrackId).Contains(credit.TrackId))
            .Select(credit => credit.ArtistId)
            .Distinct()
            .CountAsync(ct);

        return new StatisticsSummaryDto(
            listenedSeconds,
            plays,
            uniqueTracks,
            uniqueArtists,
            uniqueAlbums,
            byDay.Count,
            byDay.MaxBy(day => day.ListenedSeconds),
            byHour.MaxBy(hour => hour.ListenedSeconds));
    }

    private async Task<IReadOnlyList<StatisticsTrackDto>> TopTracksAsync(
        IQueryable<ListeningStat> scope, CancellationToken ct)
    {
        var top = await scope
            .GroupBy(s => s.TrackId)
            .Select(g => new
            {
                TrackId = g.Key,
                ListenedSeconds = g.Sum(s => s.ListenedSeconds),
                Plays = g.Sum(s => s.PlayCount),
            })
            .OrderByDescending(x => x.ListenedSeconds)
            .ThenByDescending(x => x.Plays)
            .Take(TopSize)
            .ToListAsync(ct);

        var tracks = await db.TracksByIdAsync(currentUser.Id, top.Select(x => x.TrackId), ct);

        return [.. top
            .Where(x => tracks.ContainsKey(x.TrackId))
            .Select(x => new StatisticsTrackDto(tracks[x.TrackId], x.ListenedSeconds, x.Plays))];
    }

    private async Task<IReadOnlyList<StatisticsEntryDto>> TopArtistsAsync(
        IQueryable<ListeningStat> scope, CancellationToken ct)
    {
        var top = await TotalsAsync(CreditsOf(scope), ct);
        var ids = top.Select(entry => entry.Id).ToList();

        var artists = await db.Artists.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new Named(a.Id, a.Name, a.ImagePath != null))
            .ToDictionaryAsync(a => a.Id, ct);

        return Describe(top, artists);
    }

    private async Task<IReadOnlyList<StatisticsEntryDto>> TopAlbumsAsync(
        IQueryable<ListeningStat> scope, CancellationToken ct)
    {
        var top = await TotalsAsync(
            scope.Where(s => s.Track!.AlbumId != null).GroupBy(s => s.Track!.AlbumId!.Value), ct);

        var ids = top.Select(entry => entry.Id).ToList();

        var albums = await db.Albums.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new Named(a.Id, a.Title, a.CoverPath != null))
            .ToDictionaryAsync(a => a.Id, ct);

        return Describe(top, albums);
    }

    private async Task<IReadOnlyList<StatisticsEntryDto>> TopGenresAsync(
        IQueryable<ListeningStat> scope, CancellationToken ct)
    {
        var top = await TotalsAsync(
            scope.Where(s => s.Track!.GenreId != null).GroupBy(s => s.Track!.GenreId!.Value), ct);

        var ids = top.Select(entry => entry.Id).ToList();

        var genres = await db.Genres.AsNoTracking()
            .Where(g => ids.Contains(g.Id))
            .Select(g => new Named(g.Id, g.Name, false))
            .ToDictionaryAsync(g => g.Id, ct);

        return Describe(top, genres);
    }

    private IQueryable<IGrouping<Guid, ListeningStat>> CreditsOf(IQueryable<ListeningStat> scope) =>
        from stat in scope
        join credit in db.TrackArtists on stat.TrackId equals credit.TrackId
        group stat by credit.ArtistId;

    private static async Task<List<Totals>> TotalsAsync(
        IQueryable<IGrouping<Guid, ListeningStat>> groups, CancellationToken ct)
    {
        var rows = await groups
            .Select(g => new
            {
                g.Key,
                ListenedSeconds = g.Sum(s => s.ListenedSeconds),
                Plays = g.Sum(s => s.PlayCount),
            })
            .OrderByDescending(row => row.ListenedSeconds)
            .ThenByDescending(row => row.Plays)
            .Take(TopSize)
            .ToListAsync(ct);

        return [.. rows.Select(row => new Totals(row.Key, row.ListenedSeconds, row.Plays))];
    }

    private static IReadOnlyList<StatisticsEntryDto> Describe(
        List<Totals> top, IReadOnlyDictionary<Guid, Named> names) =>
        [.. top
            .Where(entry => names.ContainsKey(entry.Id))
            .Select(entry => new StatisticsEntryDto(
                entry.Id,
                names[entry.Id].Name,
                entry.ListenedSeconds,
                entry.PlayCount,
                names[entry.Id].HasImage))];

    private record Totals(Guid Id, long ListenedSeconds, int PlayCount);

    private record Named(Guid Id, string Name, bool HasImage);

    private async Task<IReadOnlyList<DailyActivityDto>> ByDayAsync(
        DateTimeOffset? from, string timeZone, CancellationToken ct)
    {
        var rows = await db.Set<DailyActivityRow>().FromSql(
            $"""
            SELECT (date_trunc('day', hour AT TIME ZONE {timeZone}))::date AS day,
                   SUM(listened_seconds)::bigint                           AS listened_seconds,
                   SUM(play_count)::int                                    AS plays
            FROM listening_stats
            WHERE user_id = {currentUser.Id}
              AND ({from}::timestamptz IS NULL OR hour >= {from}::timestamptz)
            GROUP BY 1
            ORDER BY 1
            """).ToListAsync(ct);

        return [.. rows.Select(row => new DailyActivityDto(row.Day, row.ListenedSeconds, row.Plays))];
    }

    private async Task<IReadOnlyList<HourlyActivityDto>> ByHourAsync(
        DateTimeOffset? from, string timeZone, CancellationToken ct)
    {
        var rows = await db.Set<HourlyActivityRow>().FromSql(
            $"""
            SELECT (EXTRACT(hour FROM hour AT TIME ZONE {timeZone}))::int AS hour,
                   SUM(listened_seconds)::bigint                          AS listened_seconds,
                   SUM(play_count)::int                                   AS plays
            FROM listening_stats
            WHERE user_id = {currentUser.Id}
              AND ({from}::timestamptz IS NULL OR hour >= {from}::timestamptz)
            GROUP BY 1
            ORDER BY 1
            """).ToListAsync(ct);

        return [.. rows.Select(row => new HourlyActivityDto(row.Hour, row.ListenedSeconds, row.Plays))];
    }
}

public class DailyActivityRow
{
    public DateOnly Day { get; set; }
    public long ListenedSeconds { get; set; }
    public int Plays { get; set; }
}

public class HourlyActivityRow
{
    public int Hour { get; set; }
    public long ListenedSeconds { get; set; }
    public int Plays { get; set; }
}
