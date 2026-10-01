// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace Domain.Entities.Recommendations;

public class TrackEmbedding
{
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }

    public float[] Vector { get; set; } = [];

    public int Dimension { get; set; }

    public string ModelId { get; set; } = string.Empty;

    public string Strategy { get; set; } = string.Empty;

    public string SourceHash { get; set; } = string.Empty;

    public bool Succeeded { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset AnalyzedAt { get; set; }
}
