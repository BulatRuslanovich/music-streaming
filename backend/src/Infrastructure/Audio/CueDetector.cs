// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Numerics;

namespace Infrastructure.Audio;

public sealed record TrackCuePoints(
    double AudioStartSeconds,
    double AudioEndSeconds,
    double OutroStartSeconds,
    double? TempoBpm);

// Точки склейки трека: где на самом деле начинается и кончается звук, с какого места он уходит
// в затухание и какой у него темп. Плееры по ним убирают тишину между треками и подбирают кроссфейд.
// PCM приходит потоком (моно, SampleRate): часовой микс не держится в памяти целиком, хранятся только
// энергия и spectral flux по кадрам.
public sealed class CueDetector
{
    public const int SampleRate = 11_025;

    private const int FrameLength = 1024;
    private const int HopLength = 256;
    private const int Bins = FrameLength / 2;

    public const double HopSeconds = (double)HopLength / SampleRate;

    private const double FrameSeconds = (double)FrameLength / SampleRate;

    // Тишина — ниже −60 dBFS или на 45 дБ тише самого громкого кадра: шум винила и хвосты
    // ревербераций уходят в тишину, тихое вступление классики — нет.
    private const double SilenceFloorDb = -60;
    private const double SilenceBelowPeakDb = 45;

    // Затухание — хвост, где громкость (сглаженная по 3 с) держится на 10 дБ ниже обычной для трека.
    private const double OutroDropDb = 10;
    private const double LoudnessWindowSeconds = 3;
    private const double MaximumOutroSeconds = 30;

    private const double MinimumBpm = 60;
    private const double MaximumBpm = 200;
    private const double PreferredBpm = 120;
    private const double PriorOctaves = 1.0;
    private const int TempoHarmonics = 4;

    // Насколько пик темпа должен выделяться над остальными кандидатами (в σ), чтобы ему верить.
    private const double MinimumTempoConfidence = 2.5;

    private static readonly double[] Window =
        [.. Enumerable.Range(0, FrameLength).Select(i => 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / FrameLength))];

    private readonly float[] _buffer = new float[FrameLength];
    private readonly Complex[] _spectrum = new Complex[FrameLength];
    private readonly float[] _previousMagnitudes = new float[Bins];
    private readonly List<double> _energy = [];
    private readonly List<float> _flux = [];
    private int _filled;
    private bool _hasPrevious;

    public void Feed(ReadOnlySpan<float> samples)
    {
        foreach (var sample in samples)
        {
            _buffer[_filled++] = sample;

            if (_filled < FrameLength)
                continue;

            ProcessFrame();
            Array.Copy(_buffer, HopLength, _buffer, 0, FrameLength - HopLength);
            _filled = FrameLength - HopLength;
        }
    }

    public TrackCuePoints? Complete()
    {
        // Хвост короче кадра дополняется тишиной, иначе последние ~70 мс звука потерялись бы.
        if (_filled > FrameLength - HopLength || _energy.Count == 0)
        {
            Array.Clear(_buffer, _filled, FrameLength - _filled);
            ProcessFrame();
        }

        return Analyze([.. _energy], [.. _flux]);
    }

    private void ProcessFrame()
    {
        double sumSquares = 0;

        for (var i = 0; i < FrameLength; i++)
        {
            var sample = _buffer[i];
            sumSquares += (double)sample * sample;
            _spectrum[i] = new Complex(sample * Window[i], 0);
        }

        _energy.Add(sumSquares / FrameLength);

        Fft.Transform(_spectrum);

        // Spectral flux по логарифму амплитуды: сколько энергии прибавилось с прошлого кадра.
        // Так атаки барабанов видны и в тихих, и в громких местах.
        double flux = 0;
        for (var bin = 1; bin < Bins; bin++)
        {
            var magnitude = (float)Math.Log(1 + 100 * _spectrum[bin].Magnitude);

            if (_hasPrevious)
                flux += Math.Max(0, magnitude - _previousMagnitudes[bin]);

            _previousMagnitudes[bin] = magnitude;
        }

        _hasPrevious = true;
        _flux.Add((float)flux);
    }

    public static TrackCuePoints? Analyze(IReadOnlyList<double> energy, IReadOnlyList<float> flux)
    {
        if (energy.Count == 0)
            return null;

        var decibels = new double[energy.Count];
        var peak = double.NegativeInfinity;

        for (var i = 0; i < energy.Count; i++)
        {
            decibels[i] = 10 * Math.Log10(energy[i] + 1e-12);
            peak = Math.Max(peak, decibels[i]);
        }

        var threshold = Math.Max(SilenceFloorDb, peak - SilenceBelowPeakDb);

        var first = Array.FindIndex(decibels, db => db > threshold);
        if (first < 0)
            return null;

        var last = Array.FindLastIndex(decibels, db => db > threshold);

        var audioStart = first * HopSeconds;
        var audioEnd = Math.Min(last * HopSeconds + FrameSeconds, energy.Count * HopSeconds + FrameSeconds);

        var outroStart = OutroStart(energy, decibels, first, last, threshold, audioStart, audioEnd);
        var tempo = Tempo(flux, first, last);

        return new TrackCuePoints(audioStart, audioEnd, outroStart, tempo);
    }

    private static double OutroStart(
        IReadOnlyList<double> energy,
        double[] decibels,
        int first,
        int last,
        double threshold,
        double audioStart,
        double audioEnd)
    {
        var loud = new List<double>(last - first + 1);
        for (var i = first; i <= last; i++)
        {
            if (decibels[i] > threshold)
                loud.Add(decibels[i]);
        }

        loud.Sort();
        var typical = loud[(int)(loud.Count * 0.75)];

        var prefix = new double[energy.Count + 1];
        for (var i = 0; i < energy.Count; i++)
            prefix[i + 1] = prefix[i] + energy[i];

        var half = (int)(LoudnessWindowSeconds / 2 / HopSeconds);

        double Loudness(int frame)
        {
            var from = Math.Max(0, frame - half);
            var to = Math.Min(energy.Count, frame + half + 1);
            return 10 * Math.Log10((prefix[to] - prefix[from]) / (to - from) + 1e-12);
        }

        var frame = last;
        while (frame > first && Loudness(frame) < typical - OutroDropDb)
            frame--;

        var outro = Math.Min(audioEnd, (frame + 1) * HopSeconds);

        // Затухание — не «вторая половина трека»: тихая середина не должна превращаться в аутро.
        var earliest = Math.Max(audioStart + (audioEnd - audioStart) / 2, audioEnd - MaximumOutroSeconds);
        return Math.Max(outro, earliest);
    }

    // Темп по автокорреляции огибающей атак: для каждого кандидата 60–200 BPM складываются
    // корреляции на периоде и его кратных, а мягкий приоритет около 120 BPM выбирает между
    // «вдвое быстрее» и «вдвое медленнее». Неуверенный результат — null: лучше без темпа, чем с выдуманным.
    private static double? Tempo(IReadOnlyList<float> flux, int first, int last)
    {
        var length = last - first + 1;
        var maxLag = (int)Math.Ceiling(60 / MinimumBpm / HopSeconds * TempoHarmonics) + 2;

        if (length < maxLag * 2)
            return null;

        var envelope = new double[length];
        var smoothing = (int)(1.0 / HopSeconds);
        double running = 0;

        for (var i = 0; i < length; i++)
        {
            running += flux[first + i];
            if (i >= smoothing)
                running -= flux[first + i - smoothing];

            var localMean = running / Math.Min(i + 1, smoothing);
            envelope[i] = Math.Max(0, flux[first + i] - localMean);
        }

        var correlation = new double[maxLag + 1];
        for (var lag = 1; lag <= maxLag; lag++)
        {
            double sum = 0;
            for (var i = 0; i + lag < length; i++)
                sum += envelope[i] * envelope[i + lag];

            correlation[lag] = sum / (length - lag);
        }

        double At(double lag)
        {
            var below = (int)Math.Floor(lag);
            if (below < 1 || below + 1 > maxLag)
                return 0;

            var fraction = lag - below;
            return correlation[below] * (1 - fraction) + correlation[below + 1] * fraction;
        }

        double Score(double bpm)
        {
            var period = 60 / bpm / HopSeconds;
            double sum = 0;

            for (var harmonic = 1; harmonic <= TempoHarmonics; harmonic++)
                sum += At(period * harmonic);

            var octaves = Math.Log2(bpm / PreferredBpm) / PriorOctaves;
            return sum * Math.Exp(-0.5 * octaves * octaves);
        }

        var scores = new List<double>();
        var best = MinimumBpm;
        var bestScore = double.NegativeInfinity;

        for (var bpm = MinimumBpm; bpm <= MaximumBpm; bpm += 0.5)
        {
            var score = Score(bpm);
            scores.Add(score);

            if (score > bestScore)
            {
                bestScore = score;
                best = bpm;
            }
        }

        var mean = scores.Average();
        var deviation = Math.Sqrt(scores.Sum(score => (score - mean) * (score - mean)) / scores.Count);

        if (deviation <= 0 || (bestScore - mean) / deviation < MinimumTempoConfidence)
            return null;

        for (var bpm = best - 0.5; bpm <= best + 0.5; bpm += 0.05)
        {
            var score = Score(bpm);
            if (score > bestScore)
            {
                bestScore = score;
                best = bpm;
            }
        }

        return Math.Round(best, 1);
    }
}
