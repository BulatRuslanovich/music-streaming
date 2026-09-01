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

/// <summary>Что человек слушает в эту часть суток: доля прослушивания, энергия и жанры.</summary>
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
    /// Границы намеренно широкие: части суток нужны как грубый контекст, а не как расписание.
    /// Час — местный для слушателя, потому что вечер это вечер там, где он находится.
    /// </summary>
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

    /// <summary>Часовой пояс из настроек может оказаться неизвестным этой системе — тогда UTC.</summary>
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
