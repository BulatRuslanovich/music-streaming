// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Infrastructure.Audio;
using Xunit;

namespace MusicStreaming.UnitTests.Recommendations;

public class AudioFeatureExtractionTests
{
    private const int SampleRate = 8000;

    [Fact]
    public void Periodic_clicks_produce_a_stable_tempo()
    {
        var samples = ClickTrack(bpm: 120, seconds: 30);

        var features = AudioFeatureExtraction.Extract(samples, SampleRate);

        Assert.NotNull(features);
        Assert.NotNull(features.TempoBpm);
        Assert.InRange(features.TempoBpm.Value, 115, 125);
        Assert.True(features.TempoConfidence > 0.2);
    }

    [Fact]
    public void Spectral_brightness_distinguishes_low_and_high_tones()
    {
        var low = AudioFeatureExtraction.Extract(Tone(200), SampleRate);
        var high = AudioFeatureExtraction.Extract(Tone(2500), SampleRate);

        Assert.NotNull(low);
        Assert.NotNull(high);
        Assert.True(high.Brightness > low.Brightness + 0.3);
    }

    [Fact]
    public void Silence_has_bounded_features_and_no_tempo()
    {
        var features = AudioFeatureExtraction.Extract(new float[SampleRate * 5], SampleRate);

        Assert.NotNull(features);
        Assert.Null(features.TempoBpm);
        Assert.Equal(0, features.Energy);
        Assert.Equal(0, features.Brightness);
        Assert.InRange(features.LoudnessDb, -100, 0);
    }

    private static float[] Tone(double frequency)
    {
        var samples = new float[SampleRate * 5];
        for (var index = 0; index < samples.Length; index++)
            samples[index] = (float)(0.5 * Math.Sin(2 * Math.PI * frequency * index / SampleRate));
        return samples;
    }

    private static float[] ClickTrack(double bpm, int seconds)
    {
        var samples = new float[SampleRate * seconds];
        var interval = (int)Math.Round(SampleRate * 60 / bpm);

        for (var start = 0; start < samples.Length; start += interval)
        {
            for (var offset = 0; offset < 120 && start + offset < samples.Length; offset++)
                samples[start + offset] = (float)(0.9 * Math.Exp(-offset / 24.0));
        }

        return samples;
    }
}
