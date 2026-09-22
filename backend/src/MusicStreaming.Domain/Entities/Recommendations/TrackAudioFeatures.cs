// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Entities.Recommendations;

public class TrackAudioFeatures
{
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }
    public double? TempoBpm { get; set; }
    public double TempoConfidence { get; set; }

    /// <summary>
    /// Percussive activity: normalised spectral flux.
    /// </summary>
    /// <remarks>
    /// Не функция от <see cref="LoudnessDb"/> — иначе громкость вошла бы в оценку дважды.
    /// </remarks>
    public double Energy { get; set; }
    public double LoudnessDb { get; set; }
    public double Brightness { get; set; }
    public double DynamicRangeDb { get; set; }

    /// <summary>Key 0..11 (0 = C) and mode; null when the estimate is not confident.</summary>
    public int? Key { get; set; }
    public bool IsMinor { get; set; }
    public double KeyStrength { get; set; }
    public int AlgorithmVersion { get; set; }
    public bool Succeeded { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset AnalyzedAt { get; set; }
}
