// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Globalization;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Common;

/// <summary>
/// Календарный месяц итогов и его границы в UTC.
///
/// Месяц не выбирается — он наступает. Итоги живут первые семь дней нового месяца в поясе
/// слушателя и всегда показывают предыдущий: событие, которого ждут, а не фильтр в статистике.
/// </summary>
public record RecapMonth(string Month, DateTimeOffset From, DateTimeOffset Until)
{
    public const int WindowDays = 7;

    /// <summary>Итоги прошлого месяца, пока открыто окно; иначе null.</summary>
    public static RecapMonth? Open(string timeZone, DateTimeOffset now)
    {
        var zone = Dayparts.ZoneOrUtc(timeZone);
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        if (localNow.Day > WindowDays) return null;

        var current = new DateTime(localNow.Year, localNow.Month, 1);
        return Between(current.AddMonths(-1), zone);
    }

    /// <summary>Месяц перед данным — с чем сравниваем время прослушивания и жанр.</summary>
    public static RecapMonth Before(RecapMonth month, string timeZone)
    {
        var zone = Dayparts.ZoneOrUtc(timeZone);
        var start = DateTime.ParseExact(month.Month, "yyyy-MM", CultureInfo.InvariantCulture);
        return Between(start.AddMonths(-1), zone);
    }

    // Каждая граница переводится в UTC отдельно: у месяца, внутри которого переводят часы,
    // начало и конец лежат на разных смещениях, и общий сдвиг увёл бы одну из них на час.
    private static RecapMonth Between(DateTime start, TimeZoneInfo zone) =>
        new(start.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(start, zone)),
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(start.AddMonths(1), zone)));
}
