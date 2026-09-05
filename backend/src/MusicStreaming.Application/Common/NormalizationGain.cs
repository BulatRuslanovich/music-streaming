// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Abstractions;

namespace MusicStreaming.Application.Common;

public static class NormalizationGain
{
    public static double Calculate(IReadOnlyList<(LoudnessMeasurement Measurement, int Seconds)> tracks)
    {
        if (tracks.Count == 0 || tracks.Any(t => !double.IsFinite(t.Measurement.IntegratedLufs) ||
                !double.IsFinite(t.Measurement.TruePeakDb))) return 1;
        var total = tracks.Sum(t => (double)Math.Max(1, t.Seconds));
        var energy = tracks.Sum(t => Math.Pow(10, t.Measurement.IntegratedLufs / 10) * Math.Max(1, t.Seconds)) / total;
        var loudness = 10 * Math.Log10(energy);
        // Общий коэффициент альбома сохраняет перепады. Запас по true peak ограничивает усиление.
        var db = Math.Min(Math.Clamp(-16 - loudness, -24, 6), -2 - tracks.Max(t => t.Measurement.TruePeakDb));
        return Math.Pow(10, db / 20);
    }
}
