// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class UploadCheckTests(RecommendationApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private const string KnownHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task A_file_the_library_already_has_is_recognised_by_its_hash()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        var trackId = await StampHashAsync(library.Track(0), KnownHash);

        var result = await CheckAsync(client, new UploadProbeFileDto("whatever.mp3", KnownHash, null, null));

        var verdict = Assert.Single(result.Files);
        Assert.Equal(UploadProbeVerdict.Duplicate, verdict.Verdict);
        Assert.Equal(UploadProbeBasis.Hash, verdict.Basis);
        Assert.Equal(trackId, verdict.Match?.Id);
    }

    [Fact]
    public async Task The_same_song_in_a_different_file_is_recognised_by_its_tags()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        var track = await LoadTrackAsync(library.Track(3));

        var result = await CheckAsync(
            client,
            new UploadProbeFileDto("rip.mp3", null, track.Title, track.ArtistName));

        var verdict = Assert.Single(result.Files);
        Assert.Equal(UploadProbeVerdict.Similar, verdict.Verdict);
        Assert.Equal(UploadProbeBasis.Tags, verdict.Basis);
        Assert.Equal(track.Id, verdict.Match?.Id);
    }

    [Fact]
    public async Task Tags_are_matched_the_way_the_library_normalises_them()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        var track = await LoadTrackAsync(library.Track(2));

        var result = await CheckAsync(
            client,
            new UploadProbeFileDto("rip.mp3", null, $"  {track.Title.ToUpperInvariant()} ", track.ArtistName));

        Assert.Equal(UploadProbeVerdict.Similar, Assert.Single(result.Files).Verdict);
    }

    [Fact]
    public async Task A_matching_title_alone_is_not_enough()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        var track = await LoadTrackAsync(library.Track(1));

        var result = await CheckAsync(
            client,
            new UploadProbeFileDto("rip.mp3", null, track.Title, "Somebody Else Entirely"),
            new UploadProbeFileDto("untagged.mp3", null, track.Title, null));

        Assert.All(result.Files, file => Assert.Equal(UploadProbeVerdict.New, file.Verdict));
    }

    [Fact]
    public async Task An_unknown_file_is_reported_as_new()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync();

        var result = await CheckAsync(
            client,
            new UploadProbeFileDto("new.mp3", new string('f', 64), "Nothing Like It", "Nobody At All"));

        var verdict = Assert.Single(result.Files);
        Assert.Equal(UploadProbeVerdict.New, verdict.Verdict);
        Assert.Equal(UploadProbeBasis.Hash, verdict.Basis);
        Assert.Null(verdict.Match);
    }

    [Fact]
    public async Task A_malformed_hash_is_ignored_rather_than_rejected()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await StampHashAsync(library.Track(0), KnownHash);

        var result = await CheckAsync(
            client,
            new UploadProbeFileDto("odd.mp3", "not-a-hash", null, null),
            new UploadProbeFileDto("odd2.mp3", KnownHash.ToUpperInvariant(), null, null));

        Assert.Equal(UploadProbeVerdict.New, result.Files[0].Verdict);
        Assert.Equal(UploadProbeBasis.None, result.Files[0].Basis);

        Assert.Equal(UploadProbeVerdict.Duplicate, result.Files[1].Verdict);
        Assert.Equal(UploadProbeBasis.Hash, result.Files[1].Basis);
    }

    [Fact]
    public async Task Answers_come_back_in_the_order_they_were_asked()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await StampHashAsync(library.Track(0), KnownHash);

        var result = await CheckAsync(
            client,
            new UploadProbeFileDto("a.mp3", null, "Nothing Like It", "Nobody At All"),
            new UploadProbeFileDto("b.mp3", KnownHash, null, null),
            new UploadProbeFileDto("c.mp3", null, null, null));

        Assert.Equal(["a.mp3", "b.mp3", "c.mp3"], result.Files.Select(f => f.FileName));
        Assert.Equal(
            [UploadProbeVerdict.New, UploadProbeVerdict.Duplicate, UploadProbeVerdict.New],
            result.Files.Select(f => f.Verdict));
    }

    [Fact]
    public async Task An_empty_request_asks_nothing_of_the_database()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync();

        var result = await CheckAsync(client);

        Assert.Empty(result.Files);
    }

    [Fact]
    public async Task A_verdict_admits_how_much_of_the_file_was_compared()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync();

        var result = await CheckAsync(
            client,
            new UploadProbeFileDto("hashed.mp3", new string('f', 64), null, null),
            new UploadProbeFileDto("tagged.mp3", null, "Nothing Like It", "Nobody At All"),
            new UploadProbeFileDto("bare.mp3", null, null, null));

        Assert.All(result.Files, file => Assert.Equal(UploadProbeVerdict.New, file.Verdict));
        Assert.Equal(
            [UploadProbeBasis.Hash, UploadProbeBasis.Tags, UploadProbeBasis.None],
            result.Files.Select(f => f.Basis));
    }

    [Fact]
    public async Task A_batch_larger_than_the_limit_is_refused_rather_than_answered_wrongly()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync();

        var files = Enumerable
            .Range(0, UploadProbeService.MaxFiles + 1)
            .Select(index => new UploadProbeFileDto($"{index}.mp3", null, null, null))
            .ToArray();

        var response = await PostAsync(client, files);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<UploadProbeResultDto> CheckAsync(
        HttpClient client, params UploadProbeFileDto[] files)
    {
        var response = await PostAsync(client, files);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<UploadProbeResultDto>(Json))!;
    }

    private static Task<HttpResponseMessage> PostAsync(
        HttpClient client, params UploadProbeFileDto[] files) =>
        client.PostAsJsonAsync("/api/tracks/upload/check", new UploadProbeRequest(files), Json);

    private async Task<Guid> StampHashAsync(Guid trackId, string hash)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await db.Tracks.Where(t => t.Id == trackId).ExecuteUpdateAsync(
            update => update.SetProperty(t => t.ContentHash, hash));

        return trackId;
    }

    private async Task<(Guid Id, string Title, string ArtistName)> LoadTrackAsync(Guid trackId)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var track = await db.Tracks
            .Where(t => t.Id == trackId)
            .Select(t => new { t.Id, t.Title, ArtistName = t.Artist!.Name })
            .SingleAsync();

        return (track.Id, track.Title, track.ArtistName);
    }
}
