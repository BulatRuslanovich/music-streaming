// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Entities.Recommendations;

public class TrackAudioFeatures
{
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }
    public double? TempoBpm { get; set; }
    public double TempoConfidence { get; set; }
    public double Energy { get; set; }
    public double LoudnessDb { get; set; }
    public double Brightness { get; set; }
    public double DynamicRangeDb { get; set; }
    public double AnalyzedSeconds { get; set; }
    public int AlgorithmVersion { get; set; }
    public bool Succeeded { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset AnalyzedAt { get; set; }
}
