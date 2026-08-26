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
            var db = ToDb(rms[frame]);
            if (db > -80)
                frameDb.Add(db);
        }

        var globalRms = Math.Sqrt(totalSquares / samples.Length);
        var loudness = ToDb(globalRms);
        var dynamicRange = DynamicRange(frameDb);
        var (tempo, confidence) = Tempo(rms, sampleRate);

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

                Fft(buffer);

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

        var edges = MelEdges(MelLowHz, nyquist, MelBandCount);
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
                    bandTotals[BandOf(edges, hz)] += magnitude;
                    Fold(chroma, hz, magnitude);
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
        var (key, isMinor, keyStrength) = Key(chroma);

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

    private static double[] MelEdges(double lowHz, double highHz, int bands)
    {
        var low = ToMel(lowHz);
        var high = ToMel(Math.Max(highHz, lowHz + 1));
        var edges = new double[bands + 1];

        for (var index = 0; index <= bands; index++)
            edges[index] = ToHz(low + (high - low) * index / bands);

        return edges;
    }

    private static int BandOf(double[] edges, double hz)
    {
        if (hz <= edges[0])
            return 0;

        for (var band = 1; band < edges.Length - 1; band++)
        {
            if (hz < edges[band])
                return band - 1;
        }

        return edges.Length - 2;
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

    // Ниже 130 Гц разрешение кадра меньше полутона, а выше 2 кГц у пятой гармоники уже нет
    // отношения к основному тону — за этими границами свёртка в хрому только шумит.
    private static void Fold(double[] chroma, double hz, double magnitude)
    {
        if (hz is < 130 or > 2000 || magnitude <= 0)
            return;

        var semitone = 12 * Math.Log2(hz / 440.0) + 69;
        var pitchClass = (int)Math.Round(semitone) % 12;

        if (pitchClass < 0)
            pitchClass += 12;

        chroma[pitchClass] += magnitude;
    }

    // Профили Крумхансл — Шмуклера: корреляция хромы с каждым из 24 поворотов.
    private static readonly double[] MajorProfile =
        [6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88];

    private static readonly double[] MinorProfile =
        [6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17];

    private static (int? Key, bool IsMinor, double Strength) Key(double[] chroma)
    {
        var total = chroma.Sum();
        if (total <= 1e-9)
            return (null, false, 0);

        var best = double.NegativeInfinity;
        var bestKey = 0;
        var bestMinor = false;

        for (var root = 0; root < 12; root++)
        {
            var major = Correlate(chroma, MajorProfile, root);
            var minor = Correlate(chroma, MinorProfile, root);

            if (major > best)
                (best, bestKey, bestMinor) = (major, root, false);

            if (minor > best)
                (best, bestKey, bestMinor) = (minor, root, true);
        }

        return best <= 0 ? (null, false, 0) : (bestKey, bestMinor, Math.Clamp(best, 0, 1));
    }

    private static double Correlate(double[] chroma, double[] profile, int root)
    {
        var chromaMean = chroma.Average();
        var profileMean = profile.Average();

        var product = 0.0;
        var left = 0.0;
        var right = 0.0;

        for (var index = 0; index < 12; index++)
        {
            var a = chroma[(index + root) % 12] - chromaMean;
            var b = profile[index] - profileMean;

            product += a * b;
            left += a * a;
            right += b * b;
        }

        return product / Math.Sqrt(Math.Max(1e-12, left * right));
    }

    private static double ToMel(double hz) => 2595 * Math.Log10(1 + hz / 700);

    private static double ToHz(double mel) => 700 * (Math.Pow(10, mel / 2595) - 1);

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
