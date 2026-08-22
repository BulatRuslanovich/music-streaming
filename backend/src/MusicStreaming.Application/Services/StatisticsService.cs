// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

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
        StatisticsPeriod period, CancellationToken ct)
    {
        var timeZone = (await settings.GetAsync(ct)).TimeZone;
        var from = await ResolveStartAsync(period, timeZone, ct);

        var scope = ScopeFrom(from);

        var byDay = await ByDayAsync(from, timeZone, ct);
        var byHour = await ByHourAsync(from, timeZone, ct);

        return new StatisticsDto(
            period,
            from,
            timeZone,
            await SummariseAsync(scope, byDay, byHour, ct),
            await TopTracksAsync(scope, TopSize, ct),
            await TopArtistsAsync(scope, ct),
            await TopAlbumsAsync(scope, ct),
            await TopGenresAsync(scope, ct),
            byDay,
            byHour);
    }

    public async Task<IReadOnlyList<StatisticsTrackDto>> TopTracksAsync(
        StatisticsPeriod period, int size, CancellationToken ct)
    {
        var timeZone = (await settings.GetAsync(ct)).TimeZone;
        var from = await ResolveStartAsync(period, timeZone, ct);

        return await TopTracksAsync(ScopeFrom(from), size, ct);
    }

    private IQueryable<ListeningStat> ScopeFrom(DateTimeOffset? from)
    {
        var scope = db.ListeningStats.AsNoTracking().Where(s => s.UserId == currentUser.Id);

        return from is { } start ? scope.Where(s => s.Hour >= start) : scope;
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
        var totals = await scope
            .GroupBy(_ => 1)
            .Select(group => new
            {
                ListenedSeconds = group.Sum(s => s.ListenedSeconds),
                Plays = group.Sum(s => s.PlayCount),
                UniqueTracks = group.Select(s => s.TrackId).Distinct().Count(),
                UniqueAlbums = group
                    .Where(s => s.Track!.AlbumId != null)
                    .Select(s => s.Track!.AlbumId)
                    .Distinct()
                    .Count(),
            })
            .SingleOrDefaultAsync(ct);

        var uniqueArtists = await db.TrackArtists
            .Where(credit => scope.Select(stat => stat.TrackId).Contains(credit.TrackId))
            .Select(credit => credit.ArtistId)
            .Distinct()
            .CountAsync(ct);

        return new StatisticsSummaryDto(
            totals?.ListenedSeconds ?? 0,
            totals?.Plays ?? 0,
            totals?.UniqueTracks ?? 0,
            uniqueArtists,
            totals?.UniqueAlbums ?? 0,
            byDay.Count,
            byDay.MaxBy(day => day.ListenedSeconds),
            byHour.MaxBy(hour => hour.ListenedSeconds));
    }

    private async Task<IReadOnlyList<StatisticsTrackDto>> TopTracksAsync(
        IQueryable<ListeningStat> scope, int size, CancellationToken ct)
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
            .Take(size)
            .ToListAsync(ct);

        var tracks = await db.TracksByIdAsync(currentUser.Id, top.Select(x => x.TrackId), ct);

        return [.. top
            .Where(x => tracks.ContainsKey(x.TrackId))
            .Select(x => new StatisticsTrackDto(tracks[x.TrackId], x.ListenedSeconds, x.Plays))];
    }

    private async Task<IReadOnlyList<StatisticsEntryDto>> TopArtistsAsync(
        IQueryable<ListeningStat> scope, CancellationToken ct)
    {
        var totals =
                from stat in scope
                join credit in db.TrackArtists on stat.TrackId equals credit.TrackId
                group stat by credit.ArtistId
                into grouped
                select new
                {
                    Id = grouped.Key,
                    ListenedSeconds = grouped.Sum(s => s.ListenedSeconds),
                    Plays = grouped.Sum(s => s.PlayCount),
                };

        return await (
                from total in totals
                join artist in db.Artists.AsNoTracking() on total.Id equals artist.Id
                orderby total.ListenedSeconds descending, total.Plays descending
                select new StatisticsEntryDto(
                    total.Id, artist.Name, total.ListenedSeconds, total.Plays, artist.ImagePath != null))
            .Take(TopSize)
            .ToListAsync(ct);
    }

    private async Task<IReadOnlyList<StatisticsEntryDto>> TopAlbumsAsync(
        IQueryable<ListeningStat> scope, CancellationToken ct)
    {
        var totals = scope
            .Where(s => s.Track!.AlbumId != null)
            .GroupBy(s => s.Track!.AlbumId!.Value)
            .Select(group => new
            {
                Id = group.Key,
                ListenedSeconds = group.Sum(s => s.ListenedSeconds),
                Plays = group.Sum(s => s.PlayCount),
            });

        return await (
                from total in totals
                join album in db.Albums.AsNoTracking() on total.Id equals album.Id
                orderby total.ListenedSeconds descending, total.Plays descending
                select new StatisticsEntryDto(
                    total.Id, album.Title, total.ListenedSeconds, total.Plays, album.CoverPath != null))
            .Take(TopSize)
            .ToListAsync(ct);
    }

    private async Task<IReadOnlyList<StatisticsEntryDto>> TopGenresAsync(
        IQueryable<ListeningStat> scope, CancellationToken ct)
    {
        var totals = scope
            .Where(s => s.Track!.GenreId != null)
            .GroupBy(s => s.Track!.GenreId!.Value)
            .Select(group => new
            {
                Id = group.Key,
                ListenedSeconds = group.Sum(s => s.ListenedSeconds),
                Plays = group.Sum(s => s.PlayCount),
            });

        return await (
                from total in totals
                join genre in db.Genres.AsNoTracking() on total.Id equals genre.Id
                orderby total.ListenedSeconds descending, total.Plays descending
                select new StatisticsEntryDto(
                    total.Id, genre.Name, total.ListenedSeconds, total.Plays, false))
            .Take(TopSize)
            .ToListAsync(ct);
    }

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
