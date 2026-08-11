using System.Text.RegularExpressions;

namespace MusicStreaming.Domain.Common;

public static partial class ArtistNames
{
    public const int MaxCredits = 12;

    public static IReadOnlyList<string> Split(string? raw)
    {
        var value = Clean(raw);
        if (value is null)
            return [];

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var part in SeparatorPattern().Split(value))
        {
            var name = Clean(part);
            if (name is null || !seen.Add(Normalize.Key(name)))
                continue;

            names.Add(name);
            if (names.Count == MaxCredits)
                break;
        }

        return names.Count == 0 ? [value] : names;
    }

    public static IReadOnlyList<string> SplitAll(IEnumerable<string?> rawValues)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in rawValues.SelectMany(Split))
        {
            if (!seen.Add(Normalize.Key(name)))
                continue;

            names.Add(name);
            if (names.Count == MaxCredits)
                break;
        }

        return names;
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var cleaned = value.Replace("\0", string.Empty).Trim(' ', '\t', '(', ')', '[', ']', '-', '_');
        return cleaned.Length == 0 ? null : cleaned;
    }

    [GeneratedRegex(
        @"\s*[;/]\s*|\s*,\s*|[\s(\[]+(?:featuring|feat|ft|versus|vs)\.?\s+|\s+[x×]\s+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeparatorPattern();
}
