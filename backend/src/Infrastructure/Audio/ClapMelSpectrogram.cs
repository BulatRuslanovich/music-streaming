// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Numerics;

namespace Infrastructure.Audio;

public static class ClapMelSpectrogram
{
    public const int SampleRate = 48_000;
    public const int FrameLength = 1024;
    public const int HopLength = 480;
    public const int MelBands = 64;

    public const int FrequencyBins = FrameLength / 2 + 1;

    public const int WindowSamples = SampleRate * 10;

    public const int Frames = WindowSamples / HopLength + 1;

    private const double Floor = 1e-10;

    private static readonly double[] Window =
        [.. Enumerable.Range(0, FrameLength).Select(i => 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / FrameLength))];

    public static float[] Compute(ReadOnlySpan<float> samples, ReadOnlySpan<float> melFilters)
    {
        if (melFilters.Length != FrequencyBins * MelBands)
        {
            throw new ArgumentException(
                $"Mel filter bank must hold {FrequencyBins} x {MelBands} floats.", nameof(melFilters));
        }

        var waveform = new float[WindowSamples];

        if (samples.Length >= WindowSamples)
        {
            samples[..WindowSamples].CopyTo(waveform);
        }
        else if (samples.Length > 0)
        {
            for (var offset = 0; offset + samples.Length <= WindowSamples; offset += samples.Length)
                samples.CopyTo(waveform.AsSpan(offset));
        }

        const int pad = FrameLength / 2;
        var padded = new float[WindowSamples + pad * 2];
        waveform.CopyTo(padded.AsSpan(pad));

        for (var i = 0; i < pad; i++)
        {
            padded[pad - 1 - i] = waveform[Math.Min(i + 1, WindowSamples - 1)];
            padded[pad + WindowSamples + i] = waveform[Math.Max(WindowSamples - 2 - i, 0)];
        }

        var power = new double[Frames * FrequencyBins];
        var buffer = new Complex[FrameLength];

        for (var frame = 0; frame < Frames; frame++)
        {
            var start = frame * HopLength;

            for (var i = 0; i < FrameLength; i++)
                buffer[i] = new Complex(padded[start + i] * Window[i], 0.0);

            Fft.Transform(buffer);

            var offset = frame * FrequencyBins;
            for (var bin = 0; bin < FrequencyBins; bin++)
            {
                var real = (float)buffer[bin].Real;
                var imaginary = (float)buffer[bin].Imaginary;
                var magnitude = Math.Sqrt((double)real * real + (double)imaginary * imaginary);

                power[offset + bin] = magnitude * magnitude;
            }
        }

        var result = new float[Frames * MelBands];

        for (var frame = 0; frame < Frames; frame++)
        {
            var powerOffset = frame * FrequencyBins;
            var melOffset = frame * MelBands;

            for (var band = 0; band < MelBands; band++)
            {
                var sum = 0.0;
                for (var bin = 0; bin < FrequencyBins; bin++)
                    sum += melFilters[bin * MelBands + band] * power[powerOffset + bin];

                result[melOffset + band] = (float)(10.0 * Math.Log10(Math.Max(Floor, sum)));
            }
        }

        return result;
    }
}
