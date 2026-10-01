// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Common;

public static class SecurityLimits
{
    public const int AccountLockoutAttempts = 10;

    public const int AccountLockoutMinutes = 15;
}
