using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Common;

/// <summary>
/// Превращает произвольный текст из поля фильтра в шаблон LIKE для нормализованных колонок.
/// Общий, чтобы фильтрация списка и поиск по всей библиотеке одинаково понимали «совпадает».
/// </summary>
public static class SearchTerm
{
    public const string EscapeChar = "\\";

    /// <summary>Шаблон для <paramref name="query"/> или null, когда искать нечего.</summary>
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
