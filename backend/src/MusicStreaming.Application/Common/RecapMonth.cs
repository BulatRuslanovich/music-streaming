// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Globalization;

namespace MusicStreaming.Application.Common;

public record RecapMonth(string Month, DateTimeOffset From, DateTimeOffset Until)
{
    public static RecapMonth Resolve(string? month, string timeZone, DateTimeOffset now)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        var current = new DateTime(localNow.Year, localNow.Month, 1);
        DateTime start;
        if (month is null) start = current.AddMonths(-1);
        else if (!DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture,
                     DateTimeStyles.None, out start) || start.Year < 1900 || start > current)
            throw new ValidationException("Choose a month between January 1900 and the current month.");

        return new RecapMonth(start.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(start, zone)),
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(start.AddMonths(1), zone)));
    }
}
