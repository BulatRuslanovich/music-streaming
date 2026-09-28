// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;

namespace MusicStreaming.Application.Options;

public class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// The one storage setting: it genuinely differs per environment — <c>/storage</c> in the
    /// container, the repository's <c>storage/</c> in development, a temporary directory in tests.
    /// </summary>
    /// <remarks>Лимиты загрузки — константы в <see cref="Common.UploadLimits"/>.</remarks>
    public string RootPath { get; set; } = "/storage";

    public static OptionsBuilder<StorageOptions> Validated(OptionsBuilder<StorageOptions> builder) => builder
        .Validate(o => !string.IsNullOrWhiteSpace(o.RootPath), "Storage:RootPath is required.");
}
