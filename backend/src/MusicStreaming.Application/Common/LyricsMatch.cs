// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Common;

/// <summary>Трек, для которого ищется текст.</summary>
public record LyricsQuery(string Title, string Artist, string? Album, int DurationSeconds);

/// <summary>
/// Запись из выдачи внешней базы текстов, приведённая к виду, не зависящему от того, чья это база.
/// </summary>
public record LyricsCandidate(
    string? Title,
    string? Artist,
    double DurationSeconds,
    bool Instrumental,
    string? Plain,
    string? Synced);

public static class LyricsMatch
{
    /// <summary>
    /// Выбирает из выдачи поиска запись, которая действительно относится к искомому треку.
    /// Название и исполнитель должны совпасть точно, длительность — уложиться в допуск; из того,
    /// что осталось, предпочтение отдаётся синхронному тексту и ближайшей длительности.
    /// </summary>
    public static LyricsCandidate? SelectBest(
        IEnumerable<LyricsCandidate> candidates, LyricsQuery query, int toleranceSeconds)
    {
        var title = Key(query.Title);
        var artist = Key(query.Artist);

        return candidates
            .Where(c => c.Title is not null && Key(c.Title) == title)
            .Where(c => c.Artist is not null && Key(c.Artist) == artist)
            .Where(c => Math.Abs(c.DurationSeconds - query.DurationSeconds) <= toleranceSeconds)
            .Where(c => c.Instrumental || HasText(c.Synced) || HasText(c.Plain))
            .OrderByDescending(c => HasText(c.Synced))
            .ThenBy(c => Math.Abs(c.DurationSeconds - query.DurationSeconds))
            .FirstOrDefault();
    }

    public static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Ключ сравнения, дополнительно слепой к апострофам. Схемы транслитерации расходятся прежде
    /// всего в мягком знаке — одна и та же группа лежит в базе и как «Korol i Shut», и как
    /// «Korol' i Shut», — и без этого половина записей отсеивалась бы на ровном месте.
    /// </summary>
    private static string Key(string value) =>
        Normalize.Key(value).Replace("'", string.Empty).Replace("\u2019", string.Empty);
}
