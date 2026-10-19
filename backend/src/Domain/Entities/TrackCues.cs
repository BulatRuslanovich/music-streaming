// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace Domain.Entities;

// Точки склейки трека для плееров: границы звука без тишины по краям, начало затухания и темп.
public class TrackCues
{
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }

    public string SourceHash { get; set; } = string.Empty;

    public int Version { get; set; }

    public bool Succeeded { get; set; }

    public double? AudioStartSeconds { get; set; }
    public double? AudioEndSeconds { get; set; }
    public double? OutroStartSeconds { get; set; }
    public double? TempoBpm { get; set; }

    public DateTimeOffset AnalyzedAt { get; set; }
}
