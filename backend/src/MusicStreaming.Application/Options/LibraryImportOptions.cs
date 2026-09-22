// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;

namespace MusicStreaming.Application.Options;

public enum ImportDisposition
{
    Delete,
    Move,
}

public class LibraryImportOptions
{
    public const string SectionName = "LibraryImport";

    public bool Enabled { get; set; } = true;

    public string Directory { get; set; } = "import";

    public int ScanIntervalSeconds { get; set; } = 300;

    public int StartupDelaySeconds { get; set; } = 20;

    public int BatchSize { get; set; } = 50;

    public int MinimumAgeSeconds { get; set; } = 15;

    public ImportDisposition AfterImport { get; set; } = ImportDisposition.Delete;

    public static OptionsBuilder<LibraryImportOptions> Validated(OptionsBuilder<LibraryImportOptions> builder) => builder
        .Validate(o => !string.IsNullOrWhiteSpace(o.Directory), "LibraryImport:Directory is required.")
        .Validate(o => !Path.IsPathRooted(o.Directory), "LibraryImport:Directory must be relative to Storage:RootPath.")
        .Validate(o => o.ScanIntervalSeconds is >= 30 and <= 86400, "LibraryImport:ScanIntervalSeconds must be between 30 and 86400.")
        .Validate(o => o.StartupDelaySeconds is >= 0 and <= 3600, "LibraryImport:StartupDelaySeconds must be between 0 and 3600.")
        .Validate(o => o.BatchSize is >= 1 and <= 1000, "LibraryImport:BatchSize must be between 1 and 1000.")
        .Validate(o => o.MinimumAgeSeconds is >= 0 and <= 3600, "LibraryImport:MinimumAgeSeconds must be between 0 and 3600.");
}
