// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;

namespace MusicStreaming.Application.Options;

public class StorageOptions
{
    public const string SectionName = "Storage";

    public string RootPath { get; set; } = "/storage";
    public long MaxUploadBytes { get; set; } = 200L * 1024 * 1024;
    public long MaxImageUploadBytes { get; set; } = 8L * 1024 * 1024;

    public static OptionsBuilder<StorageOptions> Validated(OptionsBuilder<StorageOptions> builder) => builder
        .Validate(o => !string.IsNullOrWhiteSpace(o.RootPath), "Storage:RootPath is required.")
        .Validate(o => o.MaxUploadBytes > 0, "Storage:MaxUploadBytes must be greater than zero.")
        .Validate(o => o.MaxImageUploadBytes > 0, "Storage:MaxImageUploadBytes must be greater than zero.");
}
