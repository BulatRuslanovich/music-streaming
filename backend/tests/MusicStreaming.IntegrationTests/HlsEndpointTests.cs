// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Domain.Common;
using MusicStreaming.Infrastructure.Persistence;
using MusicStreaming.Infrastructure.Storage;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class HlsEndpointTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task Hls_prepares_lazily_caps_the_master_and_serves_immutable_byte_ranges()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var factory = fixture;

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
        FileSystemHlsStorage storage;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var library = await LibrarySeeder.SeedAsync(db, artistCount: 1, tracksPerArtist: 1);
            trackId = library.Track(0);
            contentHash = db.Tracks.Single(track => track.Id == trackId).ContentHash;
            storage = (FileSystemHlsStorage)scope.ServiceProvider.GetRequiredService<IHlsStorage>();
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

        using var range = new HttpRequestMessage(HttpMethod.Get, $"/api/tracks/{trackId}/hls/low/media.m4s");
        range.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(1, 1);
        var segment = await client.SendAsync(range, Cancel.Token);
        Assert.Equal(HttpStatusCode.PartialContent, segment.StatusCode);
        Assert.Equal("audio/mp4", segment.Content.Headers.ContentType?.MediaType);
        Assert.Contains("immutable", segment.Headers.CacheControl?.Extensions.Select(item => item.Name) ?? []);
        Assert.Equal([2], await segment.Content.ReadAsByteArrayAsync(Cancel.Token));

        var variant = await client.GetAsync($"/api/tracks/{trackId}/hls/low/index.m3u8", Cancel.Token);
        Assert.Equal(HttpStatusCode.OK, variant.StatusCode);
        Assert.Contains("immutable", variant.Headers.CacheControl?.Extensions.Select(item => item.Name) ?? []);
    }

    [Fact]
    public async Task A_single_ready_variant_is_enough_to_serve_the_master()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var factory = fixture;

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
            var storage = (FileSystemHlsStorage)scope.ServiceProvider.GetRequiredService<IHlsStorage>();
            storage.DeleteTranscodes(contentHash);

            WriteVariant(storage, contentHash, AudioQuality.Low, [1, 2]);
        }

        var master = await client.GetAsync(
            $"/api/tracks/{trackId}/hls/master.m3u8?maxQuality=Normal", Cancel.Token);

        Assert.Equal(HttpStatusCode.OK, master.StatusCode);

        var playlist = await master.Content.ReadAsStringAsync(Cancel.Token);
        Assert.Contains("low/index.m3u8", playlist);
        Assert.DoesNotContain("normal/index.m3u8", playlist);
    }

    [Fact]
    public async Task A_source_no_heavier_than_the_cap_is_played_as_the_original()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = fixture.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost"),
        });
        (await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = RecommendationApiFixture.OwnerUsername, password = RecommendationApiFixture.OwnerPassword },
            Cancel.Token)).EnsureSuccessStatusCode();

        Guid trackId;

        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var library = await LibrarySeeder.SeedAsync(db, artistCount: 1, tracksPerArtist: 1);
            trackId = library.Track(0);

            var track = db.Tracks.Single(track => track.Id == trackId);
            track.Codec = "mp3";
            track.BitrateKbps = 192;
            await db.SaveChangesAsync(Cancel.Token);

            var storage = (FileSystemHlsStorage)scope.ServiceProvider.GetRequiredService<IHlsStorage>();
            storage.DeleteTranscodes(track.ContentHash);
            WriteVariant(storage, track.ContentHash, AudioQuality.Low, [1, 2]);
            WriteVariant(storage, track.ContentHash, AudioQuality.Normal, [3, 4, 5]);
        }

        var high = await client.GetAsync(
            $"/api/tracks/{trackId}/hls/master.m3u8?maxQuality=High", Cancel.Token);
        Assert.Equal(HttpStatusCode.Accepted, high.StatusCode);

        var normal = await client.GetAsync(
            $"/api/tracks/{trackId}/hls/master.m3u8?maxQuality=Normal", Cancel.Token);
        Assert.Equal(HttpStatusCode.OK, normal.StatusCode);
    }

    private static void WriteVariant(
        FileSystemHlsStorage storage, string contentHash, AudioQuality quality, byte[] segment)
    {
        var directory = storage.VariantDirectory(contentHash, quality);
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "index.m3u8"),
            "#EXTM3U\n#EXT-X-MAP:URI=\"media.m4s\",BYTERANGE=\"1@0\"\n#EXTINF:4,\n#EXT-X-BYTERANGE:1@1\nmedia.m4s\n");
        File.WriteAllBytes(Path.Combine(directory, "media.m4s"), segment);
    }
}
