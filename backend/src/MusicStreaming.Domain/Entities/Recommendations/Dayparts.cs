// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Entities.Recommendations;

public enum Daypart
{
    Morning = 0,
    Day = 1,
    Evening = 2,
    Night = 3,
}

/// <summary>What someone listens to in this part of the day: share of listening, energy and genres.</summary>
public record DaypartTaste(
    Daypart Part,
    double Share,
    double? Energy,
    IReadOnlyList<TasteEntry> TopGenres);

public static class Dayparts
{
    public static readonly IReadOnlyList<Daypart> All =
        [Daypart.Morning, Daypart.Day, Daypart.Evening, Daypart.Night];

    /// <summary>
    /// Which part of the day an hour falls in.
    /// </summary>
    /// <remarks>
    /// Границы намеренно широкие: части суток нужны как грубый контекст, а не как расписание.
    /// Час — местный для слушателя, потому что вечер это вечер там, где он находится.
    /// </remarks>
    public static Daypart Of(int localHour)
    {
        var hour = ((localHour % 24) + 24) % 24;

        return hour switch
        {
            >= 5 and < 11 => Daypart.Morning,
            >= 11 and < 17 => Daypart.Day,
            >= 17 and < 23 => Daypart.Evening,
            _ => Daypart.Night,
        };
    }

    public static Daypart Of(DateTimeOffset moment, TimeZoneInfo timeZone) =>
        Of(TimeZoneInfo.ConvertTime(moment, timeZone).Hour);

    /// <summary>The time zone from settings may be unknown to this machine; then UTC.</summary>
    public static TimeZoneInfo ZoneOrUtc(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
