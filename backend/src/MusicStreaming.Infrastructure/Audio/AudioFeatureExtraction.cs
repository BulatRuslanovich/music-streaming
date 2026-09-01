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

    private const int MelBandCount = 10;
    private const double MelLowHz = 40;
    private const double RolloffShare = 0.85;

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
            (double)samples.Length / sampleRate,
            spectral.Rolloff,
            spectral.Timbre,
            spectral.Key,
            spectral.IsMinor,
            spectral.KeyStrength);
    }

    private record SpectralDescription(
        double Energy,
        double Brightness,
        double Rolloff,
        IReadOnlyList<double> Timbre,
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

        var edges = PitchAnalysis.MelEdges(MelLowHz, nyquist, MelBandCount);
        var bandTotals = new double[MelBandCount];
        var chroma = new double[12];

        var centroidWeighted = 0.0;
        var rolloffSum = 0.0;
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

                    var hz = bin * sampleRate / (double)FrameSize;
                    bandTotals[PitchAnalysis.BandOf(edges, hz)] += magnitude;
                    PitchAnalysis.Fold(chroma, hz, magnitude);
                }

                magnitudeTotal += frameTotal;
                rolloffSum += RolloffBin(magnitudes, frameTotal) * sampleRate / FrameSize;
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
            return new SpectralDescription(0, 0, 0, new double[MelBandCount], null, false, 0);

        var centroidHz = centroidWeighted / magnitudeTotal * sampleRate / FrameSize;
        var flux = fluxCount == 0 ? 0 : fluxSum / fluxCount;
        var (key, isMinor, keyStrength) = PitchAnalysis.Key(chroma);

        return new SpectralDescription(
            Math.Clamp(flux / FluxReference, 0, 1),
            Math.Clamp(centroidHz / nyquist, 0, 1),
            Math.Clamp(rolloffSum / frames / nyquist, 0, 1),
            Timbre(bandTotals),
            key,
            isMinor,
            keyStrength);
    }

    /// <summary>
    /// Тембр это форма спектра, а не его уровень: логарифм полос, снятое среднее и нормировка
    /// делают вектор независимым от громкости, а близость двух треков — скалярным произведением.
    /// </summary>
    private static double[] Timbre(double[] bandTotals)
    {
        var total = bandTotals.Sum();
        if (total <= 0)
            return new double[bandTotals.Length];

        // Пол берётся от самого сигнала, а не константой: иначе тихая копия той же записи давала
        // бы другой вектор, и вся затея с независимостью от громкости разваливалась бы на пустых полосах.
        var floor = total / bandTotals.Length * 1e-6;

        var log = new double[bandTotals.Length];
        var mean = 0.0;

        for (var band = 0; band < bandTotals.Length; band++)
        {
            log[band] = Math.Log(bandTotals[band] + floor);
            mean += log[band];
        }

        mean /= bandTotals.Length;

        var norm = 0.0;
        for (var band = 0; band < log.Length; band++)
        {
            log[band] -= mean;
            norm += log[band] * log[band];
        }

        norm = Math.Sqrt(norm);
        if (norm < 1e-9)
            return new double[bandTotals.Length];

        for (var band = 0; band < log.Length; band++)
            log[band] /= norm;

        return log;
    }

    private static double RolloffBin(double[] magnitudes, double frameTotal)
    {
        if (frameTotal <= 1e-9)
            return 0;

        var target = frameTotal * RolloffShare;
        var running = 0.0;

        for (var bin = 1; bin < magnitudes.Length; bin++)
        {
            running += magnitudes[bin];
            if (running >= target)
                return bin;
        }

        return magnitudes.Length - 1;
    }

}
