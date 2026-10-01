// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace Domain.Common;

public static class Normalize
{
    public static string Key(string value) =>
        string.Join(' ', value.Trim().ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public static string Username(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();
}
