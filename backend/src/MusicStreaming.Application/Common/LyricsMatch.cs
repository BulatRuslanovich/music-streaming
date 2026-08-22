// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Common;

public record LyricsQuery(string Title, string Artist, string? Album, int DurationSeconds);

public record LyricsCandidate(
    string? Title,
    string? Artist,
    double DurationSeconds,
    bool Instrumental,
    string? Plain,
    string? Synced);

public static class LyricsMatch
{
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

    private static string Key(string value) =>
        Normalize.Key(value).Replace("'", string.Empty).Replace("\u2019", string.Empty);
}
