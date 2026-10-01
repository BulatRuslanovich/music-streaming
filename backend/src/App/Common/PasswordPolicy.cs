// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Common;

public static class PasswordPolicy
{
    private const int MinLength = 8;
    private const int MaxLength = 30;

    public static string Validate(string? password)
    {
        var value = password ?? string.Empty;

        return value.Length is >= MinLength and <= MaxLength
            ? value
            : throw new ValidationException($"The password must be {MinLength}-{MaxLength} characters long.");
    }
}
