// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Abstractions;

public record AudioFeatureVector(
    double? TempoBpm,
    double TempoConfidence,
    double Energy,
    double LoudnessDb,
    double Brightness,
    double DynamicRangeDb,
    double AnalyzedSeconds);

public interface IAudioFeatureAnalyzer
{
    bool IsAvailable { get; }

    Task<AudioFeatureVector?> AnalyzeAsync(
        string sourceAbsolutePath,
        CancellationToken cancellationToken = default);
}
