// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Options;
using MusicStreaming.Infrastructure.Audio;
using Xunit;

namespace MusicStreaming.UnitTests.Recommendations;

public class FfmpegAudioFeatureAnalyzerTests
{
    [Fact]
    public async Task Ffmpeg_adapter_decodes_a_real_wave_file()
    {
        var analyzer = new FfmpegAudioFeatureAnalyzer(
            Options.Create(new TranscodeOptions { FfmpegPath = "ffmpeg" }),
            Options.Create(new AudioAnalysisOptions { SampleRateHz = 8000, MaximumSeconds = 30 }),
            NullLogger<FfmpegAudioFeatureAnalyzer>.Instance);

        Assert.SkipUnless(analyzer.IsAvailable, "ffmpeg is not installed in this test environment");

        var path = Path.Combine(Path.GetTempPath(), $"caimack-audio-{Guid.CreateVersion7():N}.wav");
        try
        {
            WriteClickTrack(path, bpm: 120, seconds: 20);

            var features = await analyzer.AnalyzeAsync(path, TestContext.Current.CancellationToken);

            Assert.NotNull(features);
            Assert.NotNull(features.TempoBpm);
            Assert.InRange(features.TempoBpm.Value, 115, 125);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void WriteClickTrack(string path, double bpm, int seconds)
    {
        const int sampleRate = 8000;
        const short channels = 1;
        const short bitsPerSample = 16;
        var sampleCount = sampleRate * seconds;
        var dataBytes = sampleCount * sizeof(short);
        var interval = (int)Math.Round(sampleRate * 60 / bpm);

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataBytes);
        writer.Write("WAVEfmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(dataBytes);

        for (var index = 0; index < sampleCount; index++)
        {
            var offset = index % interval;
            var sample = offset < 120 ? 0.85 * Math.Exp(-offset / 24.0) : 0;
            writer.Write((short)(sample * short.MaxValue));
        }
    }
}
