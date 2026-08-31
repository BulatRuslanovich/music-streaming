// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Common;

public readonly record struct SearchTerm(string Value, string Pattern)
{
    public const string EscapeChar = "\\";

    /// <summary>
    /// Минимальная длина запроса.
    /// </summary>
    /// <remarks>
    /// Поиск идёт через LIKE с ведущим подстановочным знаком, и обслуживают его GIN-индексы
    /// gin_trgm_ops. Триграмма — это три символа: на запросе короче индекс селективности не даёт,
    /// и планировщик уходит в последовательное сканирование всех треков, альбомов и артистов.
    /// А набирается такой запрос по дороге к нормальному — то есть на каждом первом и втором
    /// нажатии клавиши мы клали базу ради заведомо бесполезной выдачи.
    /// </remarks>
    public const int MinimumLength = 3;

    public static SearchTerm? For(string? query)
    {
        var value = Normalize.Key(query ?? string.Empty);
        return value.Length == 0 ? null : new SearchTerm(value, $"%{Escape(value)}%");
    }

    /// <summary>
    /// Термин для поиска: слишком короткий запрос отбрасывается.
    /// </summary>
    /// <remarks>
    /// Отдельно от <see cref="For"/> намеренно. У фильтров каталога отсутствие термина означает
    /// «фильтра нет», и порог там превратил бы два набранных символа в показ всей библиотеки.
    /// У поиска отсутствие термина означает пустую выдачу — только там порог и уместен.
    /// </remarks>
    public static SearchTerm? ForSearch(string? query) =>
        For(query) is { } term && term.Value.Length >= MinimumLength ? term : null;

    private static string Escape(string term) => term
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");
}
