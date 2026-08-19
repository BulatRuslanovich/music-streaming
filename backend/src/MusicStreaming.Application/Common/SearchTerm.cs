// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Common;

public readonly record struct SearchTerm(string Value, string Pattern)
{
    public const string EscapeChar = "\\";

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
