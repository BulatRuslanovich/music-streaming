// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Common;

public static class PasswordPolicy
{
    public const int MinLength = 4;
    public const int MaxLength = 15;

    public static string Validate(string? password)
    {
        var value = password ?? string.Empty;

        return value.Length is >= MinLength and <= MaxLength
            ? value
            : throw new ValidationException($"The password must be {MinLength}-{MaxLength} characters long.");
    }
}
