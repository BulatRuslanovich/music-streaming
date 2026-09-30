// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Common;

public readonly record struct SearchTerm(string Value, string Pattern)
{
    public const string EscapeChar = "\\";

    public const int MinimumLength = 3;

    public static SearchTerm? For(string? query)
    {
        var value = Normalize.Key(query ?? string.Empty);
        if (value.Length == 0)
            return null;

        var escaped = value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        return new SearchTerm(value, $"%{escaped}%");
    }

    public static SearchTerm? ForSearch(string? query) =>
        For(query) is { } term && term.Value.Length >= MinimumLength ? term : null;
}
