// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Options;

public class LibraryEnrichmentOptions
{
    public const string SectionName = "LibraryEnrichment";

    public bool Enabled { get; set; } = true;
}
