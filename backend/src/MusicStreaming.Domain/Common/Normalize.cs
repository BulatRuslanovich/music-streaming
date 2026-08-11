namespace MusicStreaming.Domain.Common;

public static class Normalize
{
    public static string Key(string value) =>
        string.Join(' ', (value ?? string.Empty).Trim().ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
