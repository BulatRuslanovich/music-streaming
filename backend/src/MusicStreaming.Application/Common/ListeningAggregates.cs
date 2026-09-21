// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Common;

/// <summary>
/// Разворот прослушиваний одного слушателя: топы и активность по дням и часам.
/// </summary>
/// <remarks>
/// Слушатель смотрит свою статистику, администратор — чужую, и это два разных пути с разными
/// правами, но запрос под ними один и тот же.
/// Общим здесь становится именно запрос: пользователь приходит параметром, так что решение
/// «чью статистику можно показать» остаётся у вызывающего.
/// </remarks>
public static class ListeningAggregates
{
    public static IQueryable<ListeningStat> ScopeFor(
        IApplicationDbContext db, Guid userId, DateTimeOffset? from)
    {
        var scope = db.ListeningStats.AsNoTracking().Where(s => s.UserId == userId);

        return from is { } start ? scope.Where(s => s.Hour >= start) : scope;
    }

    public static async Task<IReadOnlyList<StatisticsTrackDto>> TopTracksAsync(
        IApplicationDbContext db,
        Guid userId,
        IQueryable<ListeningStat> scope,
        int size,
        CancellationToken ct)
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

        var tracks = await db.TracksByIdAsync(userId, top.Select(x => x.TrackId), ct);

        return [.. top
            .Where(x => tracks.ContainsKey(x.TrackId))
            .Select(x => new StatisticsTrackDto(tracks[x.TrackId], x.ListenedSeconds, x.Plays))];
    }

    // Три топа выглядят почти одинаково, и дженерик напрашивается — но собрать join и проекцию
    // через Expression значит отдать транслятору EF дерево, которое он не разбирает: запрос падает
    // в рантайме. Группировка у всех трёх разная (артист приходит через кредиты, альбом и жанр
    // лежат на треке), общего остаётся сортировка в четыре строки — дешевле повторить её.
    public static async Task<IReadOnlyList<StatisticsEntryDto>> TopArtistsAsync(
        IApplicationDbContext db, IQueryable<ListeningStat> scope, int size, CancellationToken ct)
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
            .Take(size)
            .ToListAsync(ct);
    }

    public static async Task<IReadOnlyList<StatisticsEntryDto>> TopAlbumsAsync(
        IApplicationDbContext db, IQueryable<ListeningStat> scope, int size, CancellationToken ct)
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
            .Take(size)
            .ToListAsync(ct);
    }

    public static async Task<IReadOnlyList<StatisticsEntryDto>> TopGenresAsync(
        IApplicationDbContext db, IQueryable<ListeningStat> scope, int size, CancellationToken ct)
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
            .Take(size)
            .ToListAsync(ct);
    }

    /// <summary>
    /// День считает Postgres: AT TIME ZONE в LINQ не переводится, а календарный день слушателя —
    /// это день в его зоне, а не в UTC.
    /// </summary>
    public static async Task<IReadOnlyList<DailyActivityDto>> ByDayAsync(
        IApplicationDbContext db,
        Guid userId,
        DateTimeOffset? from,
        string timeZone,
        CancellationToken ct)
    {
        var rows = await db.Set<DailyActivityRow>().FromSql(
            $"""
            SELECT (date_trunc('day', hour AT TIME ZONE {timeZone}))::date AS day,
                   SUM(listened_seconds)::bigint                           AS listened_seconds,
                   SUM(play_count)::int                                    AS plays
            FROM listening_stats
            WHERE user_id = {userId}
              AND ({from}::timestamptz IS NULL OR hour >= {from}::timestamptz)
            GROUP BY 1
            ORDER BY 1
            """).ToListAsync(ct);

        return [.. rows.Select(row => new DailyActivityDto(row.Day, row.ListenedSeconds, row.Plays))];
    }

    public static async Task<IReadOnlyList<HourlyActivityDto>> ByHourAsync(
        IApplicationDbContext db,
        Guid userId,
        DateTimeOffset? from,
        string timeZone,
        CancellationToken ct)
    {
        var rows = await db.Set<HourlyActivityRow>().FromSql(
            $"""
            SELECT (EXTRACT(hour FROM hour AT TIME ZONE {timeZone}))::int AS hour,
                   SUM(listened_seconds)::bigint                          AS listened_seconds,
                   SUM(play_count)::int                                   AS plays
            FROM listening_stats
            WHERE user_id = {userId}
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
