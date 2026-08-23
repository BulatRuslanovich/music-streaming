// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Common;

public static class SearchRank
{
    public const string FunctionName = "search_rank";
    public const int Exact = 0;
    public const int Prefix = 1;
    public const int WordPrefix = 2;
    public const int Contains = 3;
    public const int Unrelated = 4;
    public static int Of(string value, string term) =>
        throw new NotSupportedException($"{FunctionName} is evaluated by the database.");

    /// <summary>
    /// Тот же ранг, но вычислимый в памяти. Нужен, чтобы выбрать «лучшее совпадение» среди голов
    /// четырёх уже отсортированных списков: <see cref="Of"/> живёт только в SQL и бросает здесь.
    /// </summary>
    /// <remarks>
    /// Обязан повторять тело SQL-функции <c>search_rank</c> из миграции <c>InitialSchema</c>.
    /// Оба входа ожидаются уже нормализованными через <c>Normalize.Key</c>.
    /// </remarks>
    public static int Evaluate(string? normalizedValue, string normalizedTerm)
    {
        if (string.IsNullOrEmpty(normalizedValue) || string.IsNullOrEmpty(normalizedTerm))
            return Unrelated;

        if (normalizedValue == normalizedTerm)
            return Exact;

        if (normalizedValue.StartsWith(normalizedTerm, StringComparison.Ordinal))
            return Prefix;

        // position(' ' || term in ' ' || value) > 0 — совпадение с начала любого слова.
        if ((" " + normalizedValue).Contains(" " + normalizedTerm, StringComparison.Ordinal))
            return WordPrefix;

        if (normalizedValue.Contains(normalizedTerm, StringComparison.Ordinal))
            return Contains;

        return Unrelated;
    }
}
