// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;

namespace MusicStreaming.Application.Options;

public class StorageOptions
{
    public const string SectionName = "Storage";

    public string RootPath { get; set; } = "/storage";

    public static OptionsBuilder<StorageOptions> Validated(OptionsBuilder<StorageOptions> builder) => builder
        .Validate(o => !string.IsNullOrWhiteSpace(o.RootPath), "Storage:RootPath is required.");
}
