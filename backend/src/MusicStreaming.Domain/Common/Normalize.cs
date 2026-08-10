namespace MusicStreaming.Domain.Common;

/// <summary>
/// Produces the canonical form used to de-duplicate names that arrive from ID3 tags,
/// where the same artist may be written as "Metallica", "metallica" or "  Metallica ".
/// </summary>
public static class Normalize
{
    public static string Key(string value) =>
        string.Join(' ', (value ?? string.Empty).Trim().ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
