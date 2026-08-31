// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Domain.Common;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class HlsEndpointTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task Hls_prepares_lazily_caps_the_master_and_serves_immutable_segments()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        using var factory = fixture.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAudioTranscoder>();
            services.AddSingleton<IAudioTranscoder>(new AvailableTranscoder());
        }));

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost"),
        });
        (await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = RecommendationApiFixture.OwnerUsername, password = RecommendationApiFixture.OwnerPassword },
            Cancel.Token)).EnsureSuccessStatusCode();

        Guid trackId;
        string contentHash;
        IMusicStorage storage;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var library = await LibrarySeeder.SeedAsync(db, artistCount: 1, tracksPerArtist: 1);
            trackId = library.Track(0);
            contentHash = db.Tracks.Single(track => track.Id == trackId).ContentHash;
            storage = scope.ServiceProvider.GetRequiredService<IMusicStorage>();
            storage.DeleteTranscodes(contentHash);
        }

        var preparing = await client.GetAsync(
            $"/api/tracks/{trackId}/hls/master.m3u8?maxQuality=High", Cancel.Token);
        Assert.Equal(HttpStatusCode.Accepted, preparing.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(2), preparing.Headers.RetryAfter?.Delta);

        WriteVariant(storage, contentHash, AudioQuality.Low, [1, 2]);
        WriteVariant(storage, contentHash, AudioQuality.Normal, [3, 4, 5]);

        var master = await client.GetAsync(
            $"/api/tracks/{trackId}/hls/master.m3u8?maxQuality=High", Cancel.Token);
        Assert.Equal(HttpStatusCode.OK, master.StatusCode);
        Assert.Equal("application/vnd.apple.mpegurl", master.Content.Headers.ContentType?.MediaType);

        var playlist = await master.Content.ReadAsStringAsync(Cancel.Token);
        Assert.Contains("low/index.m3u8", playlist);
        Assert.Contains("normal/index.m3u8", playlist);
        Assert.DoesNotContain("high/index.m3u8", playlist);

        var segment = await client.GetAsync(
            $"/api/tracks/{trackId}/hls/low/segment-00000.m4s", Cancel.Token);
        Assert.Equal(HttpStatusCode.OK, segment.StatusCode);
        Assert.Equal("audio/mp4", segment.Content.Headers.ContentType?.MediaType);
        Assert.Contains("immutable", segment.Headers.CacheControl?.Extensions.Select(item => item.Name) ?? []);
        Assert.Equal([1, 2], await segment.Content.ReadAsByteArrayAsync(Cancel.Token));

        // Вариантный плейлист — такой же VOD, как и сегменты: дописан один раз и больше не меняется.
        // Прежние 30 секунд с must-revalidate стоили лишнего round-trip на каждом старте трека.
        var variant = await client.GetAsync($"/api/tracks/{trackId}/hls/low/index.m3u8", Cancel.Token);
        Assert.Equal(HttpStatusCode.OK, variant.StatusCode);
        Assert.Contains("immutable", variant.Headers.CacheControl?.Extensions.Select(item => item.Name) ?? []);
    }

    [Fact]
    public async Task A_single_ready_variant_is_enough_to_serve_the_master()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        using var factory = fixture.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAudioTranscoder>();
            services.AddSingleton<IAudioTranscoder>(new AvailableTranscoder());
        }));

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost"),
        });
        (await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = RecommendationApiFixture.OwnerUsername, password = RecommendationApiFixture.OwnerPassword },
            Cancel.Token)).EnsureSuccessStatusCode();

        Guid trackId;
        string contentHash;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var library = await LibrarySeeder.SeedAsync(db, artistCount: 1, tracksPerArtist: 1);
            trackId = library.Track(0);
            contentHash = db.Tracks.Single(track => track.Id == trackId).ContentHash;
            var storage = scope.ServiceProvider.GetRequiredService<IMusicStorage>();
            storage.DeleteTranscodes(contentHash);

            // Только Low. Раньше гейт требовал ещё и Normal, поэтому такой трек отдавал 202 и
            // клиент откатывался на оригинал — многомегабайтный FLAC на узком канале.
            WriteVariant(storage, contentHash, AudioQuality.Low, [1, 2]);
        }

        var master = await client.GetAsync(
            $"/api/tracks/{trackId}/hls/master.m3u8?maxQuality=Normal", Cancel.Token);

        Assert.Equal(HttpStatusCode.OK, master.StatusCode);

        var playlist = await master.Content.ReadAsStringAsync(Cancel.Token);
        Assert.Contains("low/index.m3u8", playlist);
        Assert.DoesNotContain("normal/index.m3u8", playlist);
    }

    private static void WriteVariant(
        IMusicStorage storage, string contentHash, AudioQuality quality, byte[] segment)
    {
        var directory = storage.HlsVariantDirectoryFor(contentHash, quality);
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "index.m3u8"),
            "#EXTM3U\n#EXT-X-MAP:URI=\"init.mp4\"\n#EXTINF:4,\nsegment-00000.m4s\n");
        File.WriteAllBytes(Path.Combine(directory, "init.mp4"), [0]);
        File.WriteAllBytes(Path.Combine(directory, "segment-00000.m4s"), segment);
    }

    private sealed class AvailableTranscoder : IAudioTranscoder
    {
        public bool IsAvailable => true;

        public Task<bool> TranscodeToOpusAsync(
            string sourceAbsolutePath,
            string targetAbsolutePath,
            int bitrateKbps,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> TranscodeToHlsAsync(
            string sourceAbsolutePath,
            string targetDirectory,
            int bitrateKbps,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
