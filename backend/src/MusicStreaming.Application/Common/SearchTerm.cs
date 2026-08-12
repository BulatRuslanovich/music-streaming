using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Common;

/// <summary>
/// Turns free text typed into a filter box into a LIKE pattern for the normalised columns.
/// Shared so that filtering a list and searching the whole library agree on what "matches".
/// </summary>
public static class SearchTerm
{
    public const string EscapeChar = "\\";

    /// <summary>The pattern for <paramref name="query"/>, or null when there is nothing to match.</summary>
    public static string? Pattern(string? query)
    {
        var term = Normalize.Key(query ?? string.Empty);
        return term.Length == 0 ? null : $"%{Escape(term)}%";
    }

    private static string Escape(string term) => term
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");
}
