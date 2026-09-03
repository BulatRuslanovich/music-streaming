// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Dtos;

namespace MusicStreaming.Application.Common;

/// <summary>
/// Начало периода в часовом поясе слушателя. Общее для персональной и административной статистики:
/// у них разные срезы данных, но одна и та же граница дня, и расходиться она не должна.
/// </summary>
public static class StatisticsPeriods
{
    /// <summary>
    /// Граница периода или null для <see cref="StatisticsPeriod.All"/>.
    /// </summary>
    /// <remarks>
    /// Считается в Postgres, а не в .NET: смещение часового пояса на нужную дату знает база,
    /// у которой есть таблица зон, а имя зоны у нас лежит строкой в настройках пользователя.
    /// </remarks>
    public static async Task<DateTimeOffset?> StartAsync(
        IApplicationDbContext db, StatisticsPeriod period, string timeZone, CancellationToken ct)
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
}
