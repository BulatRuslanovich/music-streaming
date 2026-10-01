// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;

namespace App.Options;

public class LrclibOptions
{
    public const string SectionName = "Lrclib";

    public static string BaseUrl => "https://lrclib.net";

    public static OptionsBuilder<LrclibOptions> Validated(OptionsBuilder<LrclibOptions> builder) => builder
        .Validate(o => !string.IsNullOrWhiteSpace(BaseUrl), "Lrclib:BaseUrl is required.");
}
