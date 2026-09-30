// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Common;

/// <summary>The account lockout.</summary>
/// <remarks>
/// Константы, а не настройки: значения подобраны под домашнюю библиотеку и ни разу не менялись.
/// Тесты, которым блокировка мешает, подменяют через DI сам <see cref="Services.LoginAttemptTracker"/>.
/// </remarks>
public static class SecurityLimits
{
    /// <summary>Failed sign-ins before the account itself is locked.</summary>
    public const int AccountLockoutAttempts = 10;

    /// <summary>How long the lock lasts, and the window the failures are counted over.</summary>
    public const int AccountLockoutMinutes = 15;
}
