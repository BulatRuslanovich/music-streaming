// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;

namespace MusicStreaming.Application.Options;

public class TagEnrichmentOptions
{
    public const string SectionName = "TagEnrichment";

    public bool Enabled { get; set; } = true;

    /// <summary>Пауза между запросами к провайдеру, мс.</summary>
    public int RequestDelayMs { get; set; } = 350;

    public static OptionsBuilder<TagEnrichmentOptions> Validated(OptionsBuilder<TagEnrichmentOptions> builder) => builder
        .Validate(o => o.RequestDelayMs >= 0, "TagEnrichment:RequestDelayMs cannot be negative.");
}
