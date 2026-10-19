// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Xunit;
using Infrastructure.Audio;

namespace UnitTests;

public class CueDetectorTests
{
    private const int Rate = CueDetector.SampleRate;

    [Fact]
    public void Silence_has_no_cues()
    {
        var detector = new CueDetector();
        detector.Feed(new float[Rate * 5]);

        Assert.Null(detector.Complete());
    }

    [Fact]
    public void Silence_around_the_music_is_cut_off()
    {
        var cues = Detect(Track(leadingSilence: 2, music: 40, fadeOut: 0, trailingSilence: 3, bpm: 120));

        Assert.NotNull(cues);
        Assert.InRange(cues.AudioStartSeconds, 1.9, 2.05);
        Assert.InRange(cues.AudioEndSeconds, 41.9, 42.2);
    }

    [Fact]
    public void A_track_that_ends_on_a_hit_has_no_outro()
    {
        var cues = Detect(Track(leadingSilence: 0, music: 40, fadeOut: 0, trailingSilence: 1, bpm: 120));

        Assert.NotNull(cues);
        Assert.InRange(cues.AudioEndSeconds - cues.OutroStartSeconds, 0, 2);
    }

    [Fact]
    public void A_fade_out_is_found_where_the_music_drops_away()
    {
        var cues = Detect(Track(leadingSilence: 0, music: 50, fadeOut: 12, trailingSilence: 1, bpm: 120));

        Assert.NotNull(cues);
        Assert.InRange(cues.OutroStartSeconds, 48, 56);
        Assert.True(cues.OutroStartSeconds < cues.AudioEndSeconds - 4);
    }

    [Theory]
    [InlineData(96)]
    [InlineData(128)]
    public void The_beat_of_a_steady_pulse_is_heard(double bpm)
    {
        var cues = Detect(Track(leadingSilence: 0, music: 45, fadeOut: 0, trailingSilence: 0, bpm: bpm));

        Assert.NotNull(cues?.TempoBpm);
        Assert.InRange(cues.TempoBpm.Value, bpm - 1, bpm + 1);
    }

    // 174 и 87 BPM — один и тот же пульс через долю; для склейки годится любой из двух.
    [Fact]
    public void A_fast_pulse_may_be_heard_at_half_time()
    {
        var cues = Detect(Track(leadingSilence: 0, music: 45, fadeOut: 0, trailingSilence: 0, bpm: 174));

        Assert.NotNull(cues?.TempoBpm);
        Assert.Contains(
            [174.0, 87.0],
            expected => Math.Abs(cues.TempoBpm.Value - expected) <= 1);
    }

    [Fact]
    public void The_waveform_spans_the_whole_file_and_follows_the_loudness()
    {
        var cues = Detect(Track(leadingSilence: 10, music: 20, fadeOut: 0, trailingSilence: 10, bpm: 120));

        Assert.NotNull(cues);
        Assert.Equal(CueDetector.WaveformBars, cues.Waveform.Length);
        Assert.Equal(255, cues.Waveform.Max());

        // Четверть файла с каждого края — тишина, середина — музыка.
        var edge = CueDetector.WaveformBars / 4 - 4;
        Assert.All(cues.Waveform[..edge], peak => Assert.Equal(0, peak));
        Assert.All(cues.Waveform[^edge..], peak => Assert.Equal(0, peak));
        Assert.True(cues.Waveform[CueDetector.WaveformBars / 2] > 100);
    }

    [Fact]
    public void Noise_without_a_pulse_has_no_tempo()
    {
        var random = new Random(7);
        var noise = new float[Rate * 40];
        for (var i = 0; i < noise.Length; i++)
            noise[i] = (float)(random.NextDouble() - 0.5) * 0.4f;

        Assert.Null(Detect(noise)?.TempoBpm);
    }

    [Fact]
    public void Feeding_in_pieces_gives_the_same_cues()
    {
        var samples = Track(leadingSilence: 1, music: 30, fadeOut: 6, trailingSilence: 2, bpm: 110);

        var whole = Detect(samples);

        var pieces = new CueDetector();
        for (var offset = 0; offset < samples.Length; offset += 777)
            pieces.Feed(samples.AsSpan(offset, Math.Min(777, samples.Length - offset)));

        var piecewise = pieces.Complete();

        Assert.NotNull(whole);
        Assert.NotNull(piecewise);
        Assert.Equal(whole with { Waveform = [] }, piecewise with { Waveform = [] });
        Assert.Equal(whole.Waveform, piecewise.Waveform);
    }

    private static TrackCuePoints? Detect(float[] samples)
    {
        var detector = new CueDetector();
        detector.Feed(samples);
        return detector.Complete();
    }

    // Пульс из коротких щелчков шума поверх тихого тона: у щелчков есть атака, у тона — громкость,
    // в затухании громкость падает на 40 дБ к концу.
    private static float[] Track(double leadingSilence, double music, double fadeOut, double trailingSilence, double bpm)
    {
        var random = new Random(42);
        var lead = (int)(leadingSilence * Rate);
        var body = (int)(music * Rate);
        var fade = (int)(fadeOut * Rate);
        var samples = new float[lead + body + fade + (int)(trailingSilence * Rate)];
        var beat = 60.0 / bpm * Rate;
        var click = Rate / 50;

        for (var i = 0; i < body + fade; i++)
        {
            var gain = i < body ? 1.0 : Math.Pow(10, -2.0 * (i - body) / fade);
            var sinceBeat = i % beat;
            var hit = sinceBeat < click ? (random.NextDouble() - 0.5) * 1.6 * (1 - sinceBeat / click) : 0;
            var tone = 0.1 * Math.Sin(2 * Math.PI * 220 * i / Rate);

            samples[lead + i] = (float)(gain * (hit + tone));
        }

        return samples;
    }
}
