// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;

namespace App.Options;

public class StorageOptions
{
    public const string SectionName = "Storage";

    public static string RootPath => "/storage";

    public static OptionsBuilder<StorageOptions> Validated(OptionsBuilder<StorageOptions> builder) => builder
        .Validate(o => !string.IsNullOrWhiteSpace(RootPath), "Storage:RootPath is required.");
}
