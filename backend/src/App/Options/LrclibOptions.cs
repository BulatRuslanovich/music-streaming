// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;

namespace App.Options;

public class LrclibOptions
{
    public const string SectionName = "Lrclib";

    public string BaseUrl { get; set; } = "https://lrclib.net";

    public static OptionsBuilder<LrclibOptions> Validated(OptionsBuilder<LrclibOptions> builder) => builder
        .Validate(o => !string.IsNullOrWhiteSpace(o.BaseUrl), "Lrclib:BaseUrl is required.");
}
