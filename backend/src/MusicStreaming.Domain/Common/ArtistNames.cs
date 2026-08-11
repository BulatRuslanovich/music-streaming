using System.Text.RegularExpressions;

namespace MusicStreaming.Domain.Common;

/// <summary>
/// Splits the single string an ID3 artist tag carries into the individual performers behind it,
/// so that "BONES, Grayera" becomes two artists instead of one row nobody can browse to.
///
/// Only separators that are unambiguous in practice are recognised. "&amp;" and "+" are left
/// alone on purpose: they belong to band names ("Simon &amp; Garfunkel", "Florence + The Machine")
/// far more often than they separate collaborators, and a wrong split is harder to notice than a
/// missing one. Adding them is a one-line change to <see cref="SeparatorPattern"/>.
/// </summary>
public static partial class ArtistNames
{
    /// <summary>Upper bound on credits per track; a malformed tag cannot spawn hundreds of rows.</summary>
    public const int MaxCredits = 12;

    /// <summary>
    /// Names that legitimately contain a separator and must survive intact. Compared on the
    /// <see cref="Normalize.Key"/> form. The backfill migration carries the same list in SQL;
    /// keep the two in step.
    /// </summary>
    private static readonly HashSet<string> Composite =
    [
        "ac/dc",
        "tyler, the creator",
        "earth, wind & fire",
        "crosby, stills & nash",
        "crosby, stills, nash & young",
        "emerson, lake & palmer",
        "blood, sweat & tears",
        "peter, paul and mary",
    ];

    /// <summary>
    /// Returns the performers named by <paramref name="raw"/>, in tag order and de-duplicated.
    /// An empty or whitespace-only value yields an empty list; a value that contains no separator
    /// yields itself.
    /// </summary>
    public static IReadOnlyList<string> Split(string? raw)
    {
        var value = Clean(raw);
        if (value is null)
            return [];

        if (Composite.Contains(Normalize.Key(value)))
            return [value];

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

        // A string made of nothing but separators still has to name someone.
        return names.Count == 0 ? [value] : names;
    }

    /// <summary>Splits <paramref name="rawValues"/> and flattens the result, keeping order.</summary>
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

    /// <summary>Trims whitespace plus the bracket and dash debris that surrounds a split part.</summary>
    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var cleaned = value.Replace("\0", string.Empty).Trim(' ', '\t', '(', ')', '[', ']', '-', '_');
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <summary>
    /// The separators: ";" and "/", a comma, a bracketed or spaced "feat."/"ft."/"vs.", and a
    /// stand-alone "x". Whitespace is required on both sides of "x" so that "Malcolm X" — where
    /// nothing follows — is left whole.
    /// </summary>
    [GeneratedRegex(
        @"\s*[;/]\s*|\s*,\s*|[\s(\[]+(?:featuring|feat|ft|versus|vs)\.?\s+|\s+[x×]\s+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeparatorPattern();
}
