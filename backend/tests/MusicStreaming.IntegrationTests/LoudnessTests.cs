// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Services;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class LoudnessTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task Full_track_loudness_is_cached_without_modifying_the_original()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);
        using var scope = fixture.CreateScope();
        var analyzer = scope.ServiceProvider.GetRequiredService<ILoudnessAnalyzer>();
        var storage = scope.ServiceProvider.GetRequiredService<IMusicStorage>();
        var bytes = Tone(0.2);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var relative = $"music/{hash}.wav";
        var path = storage.ResolveForWrite(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes, Cancel.Token);

        Assert.Null(await analyzer.CachedAsync(hash, Cancel.Token));

        var measurement = await analyzer.MeasureAsync(relative, hash, Cancel.Token);
        Assert.NotNull(measurement);
        Assert.InRange(measurement.TruePeakDb, -15, -13);
        Assert.True(double.IsFinite(measurement.IntegratedLufs));

        var cache = storage.ResolveExisting($"loudness/v1/{hash}.json");
        Assert.NotNull(cache);
        var written = File.GetLastWriteTimeUtc(cache);

        Assert.Equal(measurement, await analyzer.CachedAsync(hash, Cancel.Token));
        Assert.Equal(measurement, await analyzer.MeasureAsync(relative, hash, Cancel.Token));
        Assert.Equal(written, File.GetLastWriteTimeUtc(cache));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, Cancel.Token));
    }

    /// <summary>
    /// Раньше этот путь гонял ffmpeg прямо в обработчике запроса, и в альбомном режиме столько раз,
    /// сколько треков в альбоме. Теперь незамеренное уходит в очередь, а ответ приходит сразу.
    /// </summary>
    [Fact]
    public async Task An_unmeasured_track_answers_immediately_and_queues_the_measurement()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var library = await LibrarySeeder.SeedAsync(db, artistCount: 1, tracksPerArtist: 1);
        var trackId = library.Track(0);

        var normalization = scope.ServiceProvider.GetRequiredService<NormalizationService>();

        // Про саму очередь утверждать нечего: воркер живёт в этом же хосте и может разобрать
        // её раньше проверки. Проверяется то, ради чего всё делалось, — ответ есть сразу и он
        // честно говорит, что замера пока нет.
        var answer = await normalization.GetAsync(trackId, "track", Cancel.Token);

        Assert.False(answer.Available);
        Assert.Equal(1, answer.Gain);
    }

    private static byte[] Tone(double amplitude)
    {
        const int rate = 48000;
        const int samples = rate * 4;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + samples * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples * 2);
        for (var i = 0; i < samples; i++)
            writer.Write((short)(short.MaxValue * amplitude * Math.Sin(2 * Math.PI * 440 * i / rate)));
        return stream.ToArray();
    }
}
