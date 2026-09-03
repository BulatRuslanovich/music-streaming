// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Admin;

/// <summary>
/// Разворот по одному слушателю: топы, активность по дням и часам, откуда он включает музыку и
/// что принёс в библиотеку.
/// </summary>
/// <remarks>
/// Повторяет форму <see cref="StatisticsService"/>, но для произвольного пользователя, а не для
/// текущего. Сам <see cref="StatisticsService"/> обобщать под это нельзя: он намеренно замкнут на
/// <see cref="ICurrentUser"/>, и снятие этого ограничения открыло бы чужую статистику всем.
/// </remarks>
public class AdminListenerBreakdown(IApplicationDbContext db)
{
    private const int TopSize = 10;
    private const int RecentUploads = 10;

    public async Task<AdminListenerDetailDto> BuildAsync(
        AdminListenerDto listener, AdminPeriodWindow window, CancellationToken ct)
    {
        var scope = Scope(listener.Id, window.From);

        return new AdminListenerDetailDto(
            window.Period,
            window.From,
            window.TimeZone,
            listener,
            await TopTracksAsync(listener.Id, scope, ct),
            await TopArtistsAsync(scope, ct),
            await TopAlbumsAsync(scope, ct),
            await TopGenresAsync(scope, ct),
            await ByDayAsync(listener.Id, window, ct),
            await ByHourAsync(listener.Id, window, ct),
            await BySourceAsync(listener.Id, window.From, ct),
            await RecentUploadsAsync(listener.Id, ct));
    }

    private IQueryable<ListeningStat> Scope(Guid userId, DateTimeOffset? from)
    {
        var scope = db.ListeningStats.AsNoTracking().Where(s => s.UserId == userId);

        return from is { } start ? scope.Where(s => s.Hour >= start) : scope;
    }

    private async Task<IReadOnlyList<StatisticsTrackDto>> TopTracksAsync(
        Guid userId, IQueryable<ListeningStat> scope, CancellationToken ct)
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

        var tracks = await db.TracksByIdAsync(userId, top.Select(x => x.TrackId), ct);

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
        Guid userId, AdminPeriodWindow window, CancellationToken ct)
    {
        var from = window.From;
        var timeZone = window.TimeZone;

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

        return [.. rows.Select(r => new DailyActivityDto(r.Day, r.ListenedSeconds, r.Plays))];
    }

    private async Task<IReadOnlyList<HourlyActivityDto>> ByHourAsync(
        Guid userId, AdminPeriodWindow window, CancellationToken ct)
    {
        var from = window.From;
        var timeZone = window.TimeZone;

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

        return [.. rows.Select(r => new HourlyActivityDto(r.Hour, r.ListenedSeconds, r.Plays))];
    }

    private async Task<IReadOnlyList<PlaybackSourceCountDto>> BySourceAsync(
        Guid userId, DateTimeOffset? from, CancellationToken ct)
    {
        var query = db.PlaybackEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.Type == PlaybackEventType.TrackStarted);

        if (from is { } start)
            query = query.Where(e => e.OccurredAt >= start);

        // Проекция в record поверх GroupBy обрывает трансляцию, как только на неё вешается
        // сортировка: EF не разбирает такое дерево. Группировка отдаёт анонимный тип, а DTO
        // собирается уже в памяти — строк здесь по числу мест в приложении, не больше.
        var counts = await query
            .GroupBy(e => e.Source)
            .Select(g => new { Source = g.Key, Plays = g.Count() })
            .OrderByDescending(x => x.Plays)
            .ToListAsync(ct);

        return [.. counts.Select(c => new PlaybackSourceCountDto(c.Source, c.Plays))];
    }

    /// <summary>Последнее принесённое — без ограничения периодом: список всегда должен что-то показать.</summary>
    private Task<List<AdminUploadedTrackDto>> RecentUploadsAsync(Guid userId, CancellationToken ct) =>
        db.Tracks.AsNoTracking()
            .Where(t => t.AddedByUserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .Take(RecentUploads)
            .Select(t => new AdminUploadedTrackDto(
                t.Id, t.Title, t.Artist!.Name, t.CreatedAt, t.FileSize))
            .ToListAsync(ct);
}
