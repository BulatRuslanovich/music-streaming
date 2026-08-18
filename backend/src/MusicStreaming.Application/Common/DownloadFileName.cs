using System.Text;

namespace MusicStreaming.Application.Common;

public static class DownloadFileName
{
    private const string InvalidCharacters = "\\/:*?\"<>|";
    private const int MaxBaseLength = 120;

    public static string For(string? artist, string title, string extension)
    {
        var basis = string.IsNullOrWhiteSpace(artist) ? title : $"{artist} - {title}";
        var cleaned = Clean(basis);

        return (cleaned.Length == 0 ? "track" : cleaned) + extension;
    }

    private static string Clean(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            var safe = InvalidCharacters.Contains(character) || char.IsControl(character) ? ' ' : character;

            if (safe != ' ' || (builder.Length > 0 && builder[^1] != ' '))
                builder.Append(safe);
        }

        var trimmed = builder.ToString().Trim(' ', '.');

        return trimmed.Length <= MaxBaseLength ? trimmed : trimmed[..MaxBaseLength].TrimEnd();
    }
}
