// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Numerics;
using MusicStreaming.Application.Abstractions;

namespace MusicStreaming.Infrastructure.Audio;

internal static class AudioFeatureExtraction
{
    private const int FrameSize = 1024;
    private const int HopSize = 256;

    // Спектр считается не по всему файлу, а по блокам подряд идущих кадров: поток (flux) — это
    // разница между соседними кадрами, и по кадрам, разбросанным через секунду, он не считается.
    private const int BlockCount = 64;
    private const int BlockFrames = 8;

    /// <summary>Доля потока, на которой дескриптор насыщается: выше начинается плотная перкуссия.</summary>
    private const double FluxReference = 0.30;

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
            var db = TempoAnalysis.ToDb(rms[frame]);
            if (db > -80)
                frameDb.Add(db);
        }

        var globalRms = Math.Sqrt(totalSquares / samples.Length);
        var loudness = TempoAnalysis.ToDb(globalRms);
        var dynamicRange = TempoAnalysis.DynamicRange(frameDb);
        var (tempo, confidence) = TempoAnalysis.Estimate(rms, sampleRate, HopSize);

        var blocks = Spectra(samples, frameCount);
        var spectral = Describe(blocks, sampleRate);

        return new AudioFeatureVector(
            tempo,
            confidence,
            spectral.Energy,
            Math.Clamp(loudness, -100, 0),
            spectral.Brightness,
            dynamicRange,
            spectral.Key,
            spectral.IsMinor,
            spectral.KeyStrength);
    }

    private record SpectralDescription(
        double Energy,
        double Brightness,
        int? Key,
        bool IsMinor,
        double KeyStrength);

    private static List<double[][]> Spectra(float[] samples, int frameCount)
    {
        var blocks = new List<double[][]>(BlockCount);
        var span = Math.Max(BlockFrames, frameCount / BlockCount);
        var buffer = new Complex[FrameSize];
        var bins = FrameSize / 2;

        for (var start = 0; start + BlockFrames <= frameCount; start += span)
        {
            var block = new double[BlockFrames][];

            for (var offset = 0; offset < BlockFrames; offset++)
            {
                var sample = (start + offset) * HopSize;
                Array.Clear(buffer);

                for (var index = 0; index < FrameSize && sample + index < samples.Length; index++)
                {
                    var window = 0.5 - 0.5 * Math.Cos(2 * Math.PI * index / (FrameSize - 1));
                    buffer[index] = new Complex(samples[sample + index] * window, 0);
                }

                Fft.Transform(buffer);

                var magnitudes = new double[bins];
                for (var bin = 1; bin < bins; bin++)
                    magnitudes[bin] = buffer[bin].Magnitude;

                block[offset] = magnitudes;
            }

            blocks.Add(block);
        }

        return blocks;
    }

    private static SpectralDescription Describe(List<double[][]> blocks, int sampleRate)
    {
        var bins = FrameSize / 2;
        var nyquist = sampleRate / 2.0;

        var chroma = new double[12];

        var centroidWeighted = 0.0;
        var magnitudeTotal = 0.0;
        var fluxSum = 0.0;
        var fluxCount = 0;
        var frames = 0;

        foreach (var block in blocks)
        {
            for (var index = 0; index < block.Length; index++)
            {
                var magnitudes = block[index];
                var frameTotal = 0.0;

                for (var bin = 1; bin < bins; bin++)
                {
                    var magnitude = magnitudes[bin];
                    frameTotal += magnitude;
                    centroidWeighted += magnitude * bin;

                    PitchAnalysis.Fold(chroma, bin * sampleRate / (double)FrameSize, magnitude);
                }

                magnitudeTotal += frameTotal;
                frames++;

                if (index == 0)
                    continue;

                // Поток нормирован на громкость кадра, поэтому он не повторяет loudness: тише
                // сведённая копия той же записи даёт то же значение.
                var previous = block[index - 1];
                var rise = 0.0;

                for (var bin = 1; bin < bins; bin++)
                    rise += Math.Max(0, magnitudes[bin] - previous[bin]);

                if (frameTotal > 1e-9)
                {
                    fluxSum += rise / frameTotal;
                    fluxCount++;
                }
            }
        }

        if (frames == 0 || magnitudeTotal <= 1e-9)
            return new SpectralDescription(0, 0, null, false, 0);

        var centroidHz = centroidWeighted / magnitudeTotal * sampleRate / FrameSize;
        var flux = fluxCount == 0 ? 0 : fluxSum / fluxCount;
        var (key, isMinor, keyStrength) = PitchAnalysis.Key(chroma);

        return new SpectralDescription(
            Math.Clamp(flux / FluxReference, 0, 1),
            Math.Clamp(centroidHz / nyquist, 0, 1),
            key,
            isMinor,
            keyStrength);
    }
}
