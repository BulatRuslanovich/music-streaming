// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Abstractions;

public record AudioFeatureVector(
    double? TempoBpm,
    double TempoConfidence,

    // Перкуссивная активность: нормированный спектральный поток, не громкость.
    double Energy,
    double LoudnessDb,
    double Brightness,
    double DynamicRangeDb,
    double AnalyzedSeconds,
    double SpectralRolloff,

    // Форма спектра по мел-полосам, со снятым средним и единичной нормой.
    IReadOnlyList<double> Timbre,
    int? Key,
    bool IsMinor,
    double KeyStrength);

public interface IAudioFeatureAnalyzer
{
    bool IsAvailable { get; }

    Task<AudioFeatureVector?> AnalyzeAsync(
        string sourceAbsolutePath,
        CancellationToken cancellationToken = default);
}
