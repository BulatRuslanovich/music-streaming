// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Infrastructure.Audio;

/// <summary>
/// Темп и динамика по огибающей громкости кадров: автокорреляция функции атак даёт BPM,
/// разброс уровней — динамический диапазон.
/// </summary>
internal static class TempoAnalysis
{
    public static (double? Tempo, double Confidence) Estimate(double[] rms, int sampleRate, int hopSize)
    {
        if (rms.Length < 64)
            return (null, 0);

        var onset = new double[rms.Length];
        for (var index = 1; index < rms.Length; index++)
        {
            var from = Math.Max(0, index - 8);
            var local = 0.0;
            for (var previous = from; previous < index; previous++)
                local += rms[previous];

            local /= Math.Max(1, index - from);
            onset[index] = Math.Max(0, rms[index] - local);
        }

        var onsetEnergy = onset.Sum(value => value * value);
        if (onsetEnergy < 1e-8)
            return (null, 0);

        var framesPerMinute = 60.0 * sampleRate / hopSize;
        var minimumLag = Math.Max(1, (int)Math.Floor(framesPerMinute / 190));
        var maximumLag = Math.Min(onset.Length / 3, (int)Math.Ceiling(framesPerMinute / 70));

        var bestLag = 0;
        var bestScore = double.NegativeInfinity;
        var bestCorrelation = 0.0;

        for (var lag = minimumLag; lag <= maximumLag; lag++)
        {
            var correlation = Correlation(onset, lag);
            var bpm = framesPerMinute / lag;
            var preference = 1 - 0.03 * Math.Abs(Math.Log2(bpm / 120));
            var score = correlation * preference;

            if (score <= bestScore)
                continue;

            bestScore = score;
            bestCorrelation = correlation;
            bestLag = lag;
        }

        if (bestLag == 0 || bestCorrelation < 0.08)
            return (null, Math.Clamp(bestCorrelation, 0, 1));

        return (Math.Round(framesPerMinute / bestLag, 1), Math.Clamp(bestCorrelation, 0, 1));
    }

    private static double Correlation(double[] values, int lag)
    {
        var product = 0.0;
        var left = 0.0;
        var right = 0.0;

        for (var index = lag; index < values.Length; index++)
        {
            product += values[index] * values[index - lag];
            left += values[index] * values[index];
            right += values[index - lag] * values[index - lag];
        }

        return product / Math.Sqrt(Math.Max(1e-12, left * right));
    }

    public static double DynamicRange(List<double> frameDb)
    {
        if (frameDb.Count < 2)
            return 0;

        frameDb.Sort();
        return Math.Clamp(Percentile(frameDb, 0.90) - Percentile(frameDb, 0.20), 0, 60);
    }

    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        var position = percentile * (sorted.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(sorted.Count - 1, lower + 1);
        var fraction = position - lower;
        return sorted[lower] * (1 - fraction) + sorted[upper] * fraction;
    }

    public static double ToDb(double amplitude) => 20 * Math.Log10(Math.Max(amplitude, 1e-10));
}
