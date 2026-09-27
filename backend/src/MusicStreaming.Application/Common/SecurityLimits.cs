// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Common;

/// <summary>Request rate limits and the account lockout.</summary>
/// <remarks>
/// Константы, а не настройки: значения подобраны под домашнюю библиотеку и ни разу не менялись.
/// Тесты, которым лимиты мешают, подменяют не числа в конфигурации, а сами потребители —
/// <c>RateLimits</c> и <see cref="Services.LoginAttemptTracker"/> — через DI.
/// </remarks>
public static class SecurityLimits
{
    /// <summary>Sign-in attempts per address per minute.</summary>
    public const int LoginAttemptsPerMinute = 10;

    /// <summary>Upload requests per user per minute.</summary>
    public const int UploadsPerMinute = 60;

    /// <summary>Search requests per user per minute.</summary>
    public const int SearchesPerMinute = 120;

    /// <summary>Playback event batches per user per minute.</summary>
    public const int EventsPerMinute = 120;

    /// <summary>Failed sign-ins before the account itself is locked.</summary>
    /// <remarks>
    /// Лимит по адресу бессилен против одного пароля, перебираемого с пула адресов, — для этого и
    /// нужна блокировка учётки.
    /// </remarks>
    public const int AccountLockoutAttempts = 10;

    /// <summary>How long the lock lasts, and the window the failures are counted over.</summary>
    public const int AccountLockoutMinutes = 15;
}
