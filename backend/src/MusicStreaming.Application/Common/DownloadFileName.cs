using System.Text;

namespace MusicStreaming.Application.Common;

/// <summary>
/// Имя файла для скачивания.
///
/// <para>
/// Название трека приходит из тегов, то есть из чужого файла, и попадать в заголовок
/// <c>Content-Disposition</c> как есть не должно: символы, недопустимые в файловых системах,
/// заменяются, а длина ограничивается — иначе имя, собранное из длинного названия, упрётся в предел
/// пути при сохранении.
/// </para>
/// </summary>
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
