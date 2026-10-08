// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Text.RegularExpressions;

namespace Domain.Entities.Recommendations;

// Источник прослушивания приходит от клиента как есть, поэтому принимается только короткий ключ вида
// «home:forYou» / «radio:mood:sad»: буквы, цифры и «:_-». Остальное — неизвестный источник.
public static partial class PlaybackSource
{
    public const int MaxLength = 64;

    public static string? Normalize(string? source)
    {
        var trimmed = source?.Trim();

        return string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength || !Allowed().IsMatch(trimmed)
            ? null
            : trimmed;
    }

    // Полки и радио, которые подбирает движок, а не просто показывают библиотеку.
    private static readonly HashSet<string> RecommendedShelves = new(StringComparer.Ordinal)
    {
        "home:forYou",
        "home:discover",
        "home:becauseYouListened",
        "home:artistsForYou",
        "home:dailyMix",
        "mix:daily",
    };

    // Списки самого слушателя, по которым листают подряд: скип тут часто значит «дальше», а не «не нравится».
    private static readonly HashSet<string> BrowsedLists = new(StringComparer.Ordinal)
    {
        "album",
        "artist",
        "playlist",
        "favorites",
        "history",
        "tracks",
        "genre",
        "genres",
        "downloads",
        "mix:new",
        "mix:top",
    };

    public static bool IsRecommendation(string? source) =>
        source is not null
        && (RecommendedShelves.Contains(source) || source == "radio" || source.StartsWith("radio:", StringComparison.Ordinal));

    public static bool IsBrowsing(string? source) => source is not null && BrowsedLists.Contains(source);

    [GeneratedRegex("^[A-Za-z0-9:_-]+$")]
    private static partial Regex Allowed();
}
