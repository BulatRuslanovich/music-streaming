// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Admin;

/// <summary>Сводка по сервису целиком: люди, библиотека, прослушивания и загрузки за период.</summary>
public class AdminOverviewService(IApplicationDbContext db, AdminStatisticsScope scope)
{
    public async Task<AdminOverviewDto> GetAsync(StatisticsPeriod period, CancellationToken ct)
    {
        var window = await scope.ResolveAsync(period, ct);

        return new AdminOverviewDto(
            window.Period,
            window.From,
            window.TimeZone,
            await UsersAsync(window.From, ct),
            await LibraryAsync(window.From, ct),
            await ListeningAsync(window.From, ct),
            await ActivityByDayAsync(window, ct),
            await UploadsByDayAsync(window, ct),
            await UploadsBySourceAsync(window.From, ct));
    }

    private async Task<AdminOverviewUsersDto> UsersAsync(DateTimeOffset? from, CancellationToken ct)
    {
        var total = await db.Users.AsNoTracking().CountAsync(ct);

        var active = await ListeningScope(from)
            .Select(s => s.UserId)
            .Distinct()
            .CountAsync(ct);

        var created = from is { } start
            ? await db.Users.AsNoTracking().CountAsync(u => u.CreatedAt >= start, ct)
            : total;

        return new AdminOverviewUsersDto(total, active, created);
    }

    private async Task<AdminOverviewLibraryDto> LibraryAsync(DateTimeOffset? from, CancellationToken ct)
    {
        // Одна группировка на всю библиотеку вместо четырёх отдельных COUNT/SUM.
        var totals = await db.Tracks.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Tracks = g.Count(),
                Bytes = g.Sum(t => t.FileSize),
                Duration = g.Sum(t => (long)t.DurationSeconds),
            })
            .FirstOrDefaultAsync(ct);

        var added = from is { } start
            ? await db.Tracks.AsNoTracking().CountAsync(t => t.CreatedAt >= start, ct)
            : totals?.Tracks ?? 0;

        return new AdminOverviewLibraryDto(
            totals?.Tracks ?? 0, added, totals?.Bytes ?? 0, totals?.Duration ?? 0);
    }

    private async Task<AdminOverviewListeningDto> ListeningAsync(
        DateTimeOffset? from, CancellationToken ct)
    {
        var listening = await ListeningScope(from)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                ListenedSeconds = g.Sum(s => s.ListenedSeconds),
                Plays = g.Sum(s => s.PlayCount),
                Listeners = g.Select(s => s.UserId).Distinct().Count(),
                Tracks = g.Select(s => s.TrackId).Distinct().Count(),
            })
            .FirstOrDefaultAsync(ct);

        var outcomes = await EventScope(from)
            .Where(e => e.Type == PlaybackEventType.TrackCompleted
                        || e.Type == PlaybackEventType.TrackSkipped)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Completed = g.Count(e => e.Type == PlaybackEventType.TrackCompleted),
                Skipped = g.Count(e => e.Type == PlaybackEventType.TrackSkipped),
            })
            .FirstOrDefaultAsync(ct);

        var completed = outcomes?.Completed ?? 0;
        var skipped = outcomes?.Skipped ?? 0;

        return new AdminOverviewListeningDto(
            listening?.ListenedSeconds ?? 0,
            listening?.Plays ?? 0,
            listening?.Listeners ?? 0,
            listening?.Tracks ?? 0,
            completed,
            skipped,
            SkipRates.Of(completed, skipped));
    }

    private async Task<IReadOnlyList<DailyActivityDto>> ActivityByDayAsync(
        AdminPeriodWindow window, CancellationToken ct)
    {
        var from = window.From;
        var timeZone = window.TimeZone;

        // AT TIME ZONE в LINQ не переводится, поэтому день считает Postgres — так же, как в
        // персональной статистике, только без фильтра по пользователю.
        var rows = await db.Set<DailyActivityRow>().FromSql(
            $"""
            SELECT (date_trunc('day', hour AT TIME ZONE {timeZone}))::date AS day,
                   SUM(listened_seconds)::bigint                           AS listened_seconds,
                   SUM(play_count)::int                                    AS plays
            FROM listening_stats
            WHERE ({from}::timestamptz IS NULL OR hour >= {from}::timestamptz)
            GROUP BY 1
            ORDER BY 1
            """).ToListAsync(ct);

        return [.. rows.Select(r => new DailyActivityDto(r.Day, r.ListenedSeconds, r.Plays))];
    }

    private async Task<IReadOnlyList<DailyUploadDto>> UploadsByDayAsync(
        AdminPeriodWindow window, CancellationToken ct)
    {
        var from = window.From;
        var timeZone = window.TimeZone;

        var rows = await db.Set<DailyUploadRow>().FromSql(
            $"""
            SELECT (date_trunc('day', created_at AT TIME ZONE {timeZone}))::date AS day,
                   COUNT(*)::int                                                 AS tracks,
                   COALESCE(SUM(file_size), 0)::bigint                           AS bytes
            FROM tracks
            WHERE ({from}::timestamptz IS NULL OR created_at >= {from}::timestamptz)
            GROUP BY 1
            ORDER BY 1
            """).ToListAsync(ct);

        return [.. rows.Select(r => new DailyUploadDto(r.Day, r.Tracks, r.Bytes))];
    }

    private async Task<IReadOnlyList<IngestionSourceCountDto>> UploadsBySourceAsync(
        DateTimeOffset? from, CancellationToken ct)
    {
        var query = db.Tracks.AsNoTracking();

        if (from is { } start)
            query = query.Where(t => t.CreatedAt >= start);

        var counts = await query
            .GroupBy(t => t.IngestionSource)
            .Select(g => new { Source = g.Key, Tracks = g.Count() })
            .ToListAsync(ct);

        // Отсутствующий источник — это ноль, а не пропуск: график не должен менять форму от того,
        // что за неделю никто ничего не импортировал.
        return
        [
            .. Enum.GetValues<IngestionSource>()
                .Select(source => new IngestionSourceCountDto(
                    source, counts.FirstOrDefault(c => c.Source == source)?.Tracks ?? 0)),
        ];
    }

    private IQueryable<ListeningStat> ListeningScope(DateTimeOffset? from)
    {
        var scope = db.ListeningStats.AsNoTracking();

        return from is { } start ? scope.Where(s => s.Hour >= start) : scope;
    }

    private IQueryable<PlaybackEvent> EventScope(DateTimeOffset? from)
    {
        var scope = db.PlaybackEvents.AsNoTracking();

        return from is { } start ? scope.Where(e => e.OccurredAt >= start) : scope;
    }
}

/// <summary>Строка «сколько треков добавлено в этот день». Namespace менять нельзя — он в снапшоте.</summary>
public class DailyUploadRow
{
    public DateOnly Day { get; set; }
    public int Tracks { get; set; }
    public long Bytes { get; set; }
}
