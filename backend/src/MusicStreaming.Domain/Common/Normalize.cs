// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Common;

public static class Normalize
{
    public static string Key(string value) =>
        string.Join(' ', (value ?? string.Empty).Trim().ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>
    /// Логин к каноническому виду. В отличие от <see cref="Key"/> внутренние пробелы не схлопываются:
    /// логин с пробелом внутри — это другой логин, а не тот же самый, набранный неаккуратно.
    /// </summary>
    public static string Username(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();
}
