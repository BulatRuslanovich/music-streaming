// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Abstractions;

public record LoudnessMeasurement(double IntegratedLufs, double TruePeakDb);
public interface ILoudnessAnalyzer
{
    Task<LoudnessMeasurement?> GetAsync(string filePath, string contentHash, CancellationToken ct);
}
