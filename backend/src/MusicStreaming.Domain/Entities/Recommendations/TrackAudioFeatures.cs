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
    /// Перкуссивная активность: нормированный спектральный поток, а не функция от
    /// <see cref="LoudnessDb"/> — иначе громкость вошла бы в оценку дважды.
    /// </summary>
    public double Energy { get; set; }
    public double LoudnessDb { get; set; }
    public double Brightness { get; set; }
    public double DynamicRangeDb { get; set; }

    /// <summary>Тональность 0..11 (0 = C) и лад; null, когда оценка неуверенная.</summary>
    public int? Key { get; set; }
    public bool IsMinor { get; set; }
    public double KeyStrength { get; set; }
    public int AlgorithmVersion { get; set; }
    public bool Succeeded { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset AnalyzedAt { get; set; }
}
