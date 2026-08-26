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

    [Fact]
    public void Energy_no_longer_repeats_loudness()
    {
        var loud = ClickTrack(bpm: 120, seconds: 20);
        var quiet = Attenuate(loud, 0.1f);

        var first = AudioFeatureExtraction.Extract(loud, SampleRate);
        var second = AudioFeatureExtraction.Extract(quiet, SampleRate);

        Assert.NotNull(first);
        Assert.NotNull(second);

        // Тише сведённая копия той же записи звучит так же; раньше энергия была линейной функцией
        // громкости, и эти два трека расходились по ней максимально.
        Assert.True(
            second.LoudnessDb < first.LoudnessDb - 15,
            $"the quiet copy was not quieter: {second.LoudnessDb} vs {first.LoudnessDb}");

        Assert.Equal(first.Energy, second.Energy, 2);
    }

    [Fact]
    public void Energy_separates_percussion_from_a_held_tone()
    {
        var percussive = AudioFeatureExtraction.Extract(ClickTrack(bpm: 150, seconds: 20), SampleRate);
        var sustained = AudioFeatureExtraction.Extract(Tone(440), SampleRate);

        Assert.NotNull(percussive);
        Assert.NotNull(sustained);
        Assert.True(
            percussive.Energy > sustained.Energy + 0.2,
            $"percussion {percussive.Energy:F3} did not stand out against a tone {sustained.Energy:F3}");
    }

    [Fact]
    public void Timbre_is_a_unit_vector_that_gain_does_not_move()
    {
        var loud = AudioFeatureExtraction.Extract(Tone(440), SampleRate);
        var quiet = AudioFeatureExtraction.Extract(Attenuate(Tone(440), 0.05f), SampleRate);

        Assert.NotNull(loud);
        Assert.NotNull(quiet);
        Assert.Equal(10, loud.Timbre.Count);

        Assert.Equal(1.0, Math.Sqrt(loud.Timbre.Sum(value => value * value)), 3);

        // Векторы единичные, поэтому их скалярное произведение — косинус: у той же записи, сведённой
        // тише, он должен быть единицей с точностью до округления сэмплов во float.
        Assert.True(
            Dot(loud.Timbre, quiet.Timbre) > 0.9999,
            $"gain moved the timbre vector: cos={Dot(loud.Timbre, quiet.Timbre):F6}");
    }

    [Fact]
    public void Timbre_separates_tones_that_sit_in_different_bands()
    {
        var low = AudioFeatureExtraction.Extract(Tone(200), SampleRate);
        var high = AudioFeatureExtraction.Extract(Tone(2500), SampleRate);
        var alsoLow = AudioFeatureExtraction.Extract(Tone(230), SampleRate);

        Assert.NotNull(low);
        Assert.NotNull(high);
        Assert.NotNull(alsoLow);

        Assert.True(
            Dot(low.Timbre, alsoLow.Timbre) > Dot(low.Timbre, high.Timbre),
            "two low tones were not closer in timbre than a low one and a high one");
    }

    [Fact]
    public void Rolloff_follows_the_top_of_the_spectrum()
    {
        var low = AudioFeatureExtraction.Extract(Tone(200), SampleRate);
        var high = AudioFeatureExtraction.Extract(Tone(2500), SampleRate);

        Assert.NotNull(low);
        Assert.NotNull(high);
        Assert.True(high.SpectralRolloff > low.SpectralRolloff + 0.2);
        Assert.InRange(low.SpectralRolloff, 0, 1);
    }

    [Fact]
    public void A_triad_is_read_as_its_own_key()
    {
        // A, C#, E — ля мажор; корень 9 при нумерации от C.
        var chord = Mix(Tone(440), Tone(554.37), Tone(659.26));

        var features = AudioFeatureExtraction.Extract(chord, SampleRate);

        Assert.NotNull(features);
        Assert.Equal(9, features.Key);
        Assert.False(features.IsMinor);
        Assert.True(features.KeyStrength > 0.4);
    }

    [Fact]
    public void Silence_leaves_the_new_descriptors_empty_rather_than_wrong()
    {
        var features = AudioFeatureExtraction.Extract(new float[SampleRate * 5], SampleRate);

        Assert.NotNull(features);
        Assert.Equal(0, features.SpectralRolloff);
        Assert.Null(features.Key);
        Assert.Equal(0, features.KeyStrength);
        Assert.All(features.Timbre, value => Assert.Equal(0, value));
    }

    private static double Dot(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        var total = 0.0;
        for (var index = 0; index < left.Count; index++)
            total += left[index] * right[index];

        return total;
    }

    private static float[] Attenuate(float[] samples, float gain)
    {
        var attenuated = new float[samples.Length];
        for (var index = 0; index < samples.Length; index++)
            attenuated[index] = samples[index] * gain;

        return attenuated;
    }

    private static float[] Mix(params float[][] parts)
    {
        var mixed = new float[parts[0].Length];
        for (var index = 0; index < mixed.Length; index++)
        {
            foreach (var part in parts)
                mixed[index] += part[index] / parts.Length;
        }

        return mixed;
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
