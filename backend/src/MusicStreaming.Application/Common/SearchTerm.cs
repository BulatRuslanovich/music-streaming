using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Common;

/// <summary>
/// Приведённый к общему виду поисковый запрос: то, с чем сравниваются нормализованные колонки, и
/// шаблон LIKE для отбора. Общий, чтобы фильтрация списка, поиск по библиотеке и ранжирование
/// одинаково понимали «совпадает».
/// </summary>
/// <param name="Value">Нормализованный запрос — сравнивается с нормализованными колонками напрямую.</param>
/// <param name="Pattern">Шаблон <c>%…%</c> со спецсимволами LIKE, экранированными <see cref="EscapeChar"/>.</param>
public readonly record struct SearchTerm(string Value, string Pattern)
{
    public const string EscapeChar = "\\";

    /// <summary>Запрос или <c>null</c>, когда искать нечего.</summary>
    public static SearchTerm? For(string? query)
    {
        var value = Normalize.Key(query ?? string.Empty);
        return value.Length == 0 ? null : new SearchTerm(value, $"%{Escape(value)}%");
    }

    private static string Escape(string term) => term
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");
}
