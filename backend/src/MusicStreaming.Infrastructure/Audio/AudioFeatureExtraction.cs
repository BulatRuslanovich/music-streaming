// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Numerics;
using MusicStreaming.Application.Abstractions;

namespace MusicStreaming.Infrastructure.Audio;

internal static class AudioFeatureExtraction
{
    private const int FrameSize = 1024;
    private const int HopSize = 256;
    private const int MaximumSpectralFrames = 512;

    public static AudioFeatureVector? Extract(float[] samples, int sampleRate)
    {
        if (sampleRate <= 0 || samples.Length < sampleRate)
            return null;

        var frameCount = 1 + Math.Max(0, (samples.Length - FrameSize) / HopSize);
        if (frameCount == 0)
            return null;

        var rms = new double[frameCount];
        var frameDb = new List<double>(frameCount);
        var totalSquares = 0.0;

        for (var index = 0; index < samples.Length; index++)
            totalSquares += samples[index] * samples[index];

        for (var frame = 0; frame < frameCount; frame++)
        {
            var start = frame * HopSize;
            var squares = 0.0;
            var available = Math.Min(FrameSize, samples.Length - start);

            for (var offset = 0; offset < available; offset++)
                squares += samples[start + offset] * samples[start + offset];

            rms[frame] = Math.Sqrt(squares / Math.Max(1, available));
            var db = ToDb(rms[frame]);
            if (db > -80)
                frameDb.Add(db);
        }

        var globalRms = Math.Sqrt(totalSquares / samples.Length);
        var loudness = ToDb(globalRms);
        var energy = Math.Clamp((loudness + 60) / 60, 0, 1);
        var dynamicRange = DynamicRange(frameDb);
        var brightness = Brightness(samples, sampleRate, frameCount);
        var (tempo, confidence) = Tempo(rms, sampleRate);

        return new AudioFeatureVector(
            tempo,
            confidence,
            energy,
            Math.Clamp(loudness, -100, 0),
            brightness,
            dynamicRange,
            (double)samples.Length / sampleRate);
    }

    private static (double? Tempo, double Confidence) Tempo(double[] rms, int sampleRate)
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

        var framesPerMinute = 60.0 * sampleRate / HopSize;
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

    private static double Brightness(float[] samples, int sampleRate, int frameCount)
    {
        var stride = Math.Max(1, frameCount / MaximumSpectralFrames);
        var weighted = 0.0;
        var magnitudeTotal = 0.0;
        var buffer = new Complex[FrameSize];

        for (var frame = 0; frame < frameCount; frame += stride)
        {
            var start = frame * HopSize;
            Array.Clear(buffer);

            for (var index = 0; index < FrameSize && start + index < samples.Length; index++)
            {
                var window = 0.5 - 0.5 * Math.Cos(2 * Math.PI * index / (FrameSize - 1));
                buffer[index] = new Complex(samples[start + index] * window, 0);
            }

            Fft(buffer);

            for (var bin = 1; bin < FrameSize / 2; bin++)
            {
                var magnitude = buffer[bin].Magnitude;
                weighted += magnitude * bin;
                magnitudeTotal += magnitude;
            }
        }

        if (magnitudeTotal <= 1e-9)
            return 0;

        var centroidBin = weighted / magnitudeTotal;
        var centroidHz = centroidBin * sampleRate / FrameSize;
        return Math.Clamp(centroidHz / (sampleRate / 2.0), 0, 1);
    }

    private static double DynamicRange(List<double> frameDb)
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

    private static double ToDb(double amplitude) => 20 * Math.Log10(Math.Max(amplitude, 1e-10));

    private static void Fft(Complex[] values)
    {
        for (int index = 1, reversed = 0; index < values.Length; index++)
        {
            var bit = values.Length >> 1;
            for (; (reversed & bit) != 0; bit >>= 1)
                reversed ^= bit;
            reversed ^= bit;

            if (index < reversed)
                (values[index], values[reversed]) = (values[reversed], values[index]);
        }

        for (var length = 2; length <= values.Length; length <<= 1)
        {
            var angle = -2 * Math.PI / length;
            var step = new Complex(Math.Cos(angle), Math.Sin(angle));

            for (var offset = 0; offset < values.Length; offset += length)
            {
                var rotation = Complex.One;
                for (var index = 0; index < length / 2; index++)
                {
                    var even = values[offset + index];
                    var odd = values[offset + index + length / 2] * rotation;
                    values[offset + index] = even + odd;
                    values[offset + index + length / 2] = even - odd;
                    rotation *= step;
                }
            }
        }
    }
}
