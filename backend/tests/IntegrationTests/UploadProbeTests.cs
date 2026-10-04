// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using App.Dtos;
using Infrastructure.Persistence;
using Xunit;

namespace IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class UploadProbeTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task A_file_with_the_same_title_and_artist_is_a_duplicate_whatever_its_bytes()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        var result = await ProbeAsync(client, new UploadProbeFileDto("rip.mp3", Hash('b'), " track  1 ", "ARTIST 0"));

        Assert.Equal(UploadProbeVerdict.Duplicate, result.Verdict);
        Assert.Equal(library.Track(1), result.Match?.Id);
    }

    [Fact]
    public async Task A_flac_of_a_track_kept_as_mp3_is_an_upgrade()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        var result = await ProbeAsync(client, new UploadProbeFileDto("rip.flac", Hash('b'), "Track 1", "Artist 0"));

        Assert.Equal(UploadProbeVerdict.Upgrade, result.Verdict);
        Assert.Equal(library.Track(1), result.Match?.Id);
    }

    [Fact]
    public async Task Nothing_upgrades_a_track_already_kept_lossless()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await SetCodecAsync(library.Track(1), "flac");

        var result = await ProbeAsync(client, new UploadProbeFileDto("rip.flac", Hash('b'), "Track 1", "Artist 0"));

        Assert.Equal(UploadProbeVerdict.Duplicate, result.Verdict);
    }

    [Fact]
    public async Task The_same_title_by_another_artist_is_new()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync();

        var result = await ProbeAsync(client, new UploadProbeFileDto("rip.mp3", Hash('b'), "Track 1", "Someone Else"));

        Assert.Equal(UploadProbeVerdict.New, result.Verdict);
    }

    private static string Hash(char fill) => new(fill, 64);

    private async Task SetCodecAsync(Guid trackId, string codec)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await db.Tracks
            .Where(t => t.Id == trackId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.Codec, codec), Cancel.Token);
    }

    private static async Task<UploadProbeMatchDto> ProbeAsync(HttpClient client, UploadProbeFileDto file)
    {
        var response = await client.PostAsJsonAsync(
            "/api/tracks/upload/check", new UploadProbeRequest([file]), RecommendationApiFixture.Json, Cancel.Token);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<UploadProbeResultDto>(
            RecommendationApiFixture.Json, Cancel.Token);

        return Assert.Single(result!.Files);
    }
}
