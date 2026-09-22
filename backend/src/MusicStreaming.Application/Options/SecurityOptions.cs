// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;

namespace MusicStreaming.Application.Options;

public class SecurityOptions
{
    public const string SectionName = "Security";

    public int LoginAttemptsPerMinute { get; set; } = 10;

    public int UploadsPerMinute { get; set; } = 60;

    public int SearchesPerMinute { get; set; } = 120;

    public int EventsPerMinute { get; set; } = 120;

    public int AccountLockoutAttempts { get; set; } = 10;

    public int AccountLockoutMinutes { get; set; } = 15;

    public static OptionsBuilder<SecurityOptions> Validated(OptionsBuilder<SecurityOptions> builder) => builder
        .Validate(o => o.LoginAttemptsPerMinute > 0, "Security:LoginAttemptsPerMinute must be greater than zero.")
        .Validate(o => o.UploadsPerMinute > 0, "Security:UploadsPerMinute must be greater than zero.")
        .Validate(o => o.SearchesPerMinute > 0, "Security:SearchesPerMinute must be greater than zero.")
        .Validate(o => o.EventsPerMinute > 0, "Security:EventsPerMinute must be greater than zero.")
        .Validate(o => o.AccountLockoutAttempts >= 0, "Security:AccountLockoutAttempts cannot be negative.")
        .Validate(o => o.AccountLockoutMinutes > 0, "Security:AccountLockoutMinutes must be greater than zero.");
}
