// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Net.Http.Json;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class LibraryImportTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task A_file_dropped_on_the_server_becomes_a_track_without_ever_touching_the_browser()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = TrackUploadTestClient.UniqueName("Dropped");

        Drop($"{name}/01 - {name}.mp3", ImportableMp3.Tagged(
            title: $"{name} Title", artist: $"{name} Artist", album: $"{name} Album"));

        var status = await ImportAsync(client);

        Assert.Equal(1, status.Imported);
        Assert.Equal(0, status.Failed);

        var found = await client.GetFromJsonAsync<PagedResult<TrackDto>>(
            $"/api/tracks?q={Uri.EscapeDataString($"{name} Title")}", RecommendationApiFixture.Json, Cancel.Token);

        var track = Assert.Single(found!.Items);
        Assert.Equal($"{name} Title", track.Title);
        Assert.Equal($"{name} Artist", track.ArtistName);
        Assert.Equal($"{name} Album", track.AlbumTitle);
    }

    [Fact]
    public async Task An_imported_file_leaves_the_drop_folder_so_the_next_scan_has_nothing_to_do()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = TrackUploadTestClient.UniqueName("Consumed");
        var relativePath = $"{name}/{name}.mp3";

        Drop(relativePath, ImportableMp3.Tagged(title: $"{name} Title", artist: $"{name} Artist", album: null));

        await ImportAsync(client);

        Assert.False(File.Exists(Path.Combine(ImportRoot, relativePath.Replace('/', Path.DirectorySeparatorChar))));

        var second = await ImportAsync(client);
        Assert.Equal(0, second.Imported);
        Assert.Equal(0, second.Failed);
    }

    [Fact]
    public async Task A_file_that_is_not_really_audio_is_quarantined_with_its_reason()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = TrackUploadTestClient.UniqueName("Broken");

        Drop($"{name}.mp3", "this is not an mp3"u8.ToArray());

        var status = await ImportAsync(client);

        Assert.Equal(0, status.Imported);
        Assert.Equal(1, status.Failed);
        Assert.Single(status.RecentFailures);

        Assert.True(File.Exists(Path.Combine(ImportRoot, ".failed", $"{name}.mp3")));
        Assert.True(File.Exists(Path.Combine(ImportRoot, ".failed", $"{name}.mp3.txt")));
    }

    [Fact]
    public async Task A_file_already_in_the_library_is_quarantined_rather_than_imported_twice()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = TrackUploadTestClient.UniqueName("Twice");
        var content = ImportableMp3.Tagged(title: $"{name} Title", artist: $"{name} Artist", album: null);

        Drop($"{name}-a.mp3", content);
        Assert.Equal(1, (await ImportAsync(client)).Imported);

        Drop($"{name}-b.mp3", content);
        var second = await ImportAsync(client);

        Assert.Equal(0, second.Imported);
        Assert.Equal(1, second.Failed);
        Assert.Contains("already in the library", second.RecentFailures[0].Reason);
    }

    [Fact]
    public async Task Only_admins_can_see_or_start_an_import()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var listener = await fixture.CreateSignedInClientAsync("import-listener", "listener-password-1");

        Assert.Equal(HttpStatusCode.Forbidden, (await listener.GetAsync("/api/library/import", Cancel.Token)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await listener.PostAsync("/api/library/import", null, Cancel.Token)).StatusCode);
    }

    [Fact]
    public async Task The_status_endpoint_reports_what_is_waiting_before_a_scan_starts()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = TrackUploadTestClient.UniqueName("Waiting");

        Drop($"{name}.mp3", ImportableMp3.Tagged(title: $"{name} Title", artist: $"{name} Artist", album: null));

        var status = await client.GetFromJsonAsync<LibraryImportStatusDto>(
            "/api/library/import", RecommendationApiFixture.Json, Cancel.Token);

        Assert.NotNull(status);
        Assert.True(status.Enabled);
        Assert.False(status.Running);
        Assert.True(status.Waiting >= 1, "the dropped file should be counted as waiting");

        await ImportAsync(client);
    }

    private string ImportRoot => Path.Combine(fixture.StoragePath, "import");

    private void Drop(string relativePath, byte[] content)
    {
        var absolutePath = Path.Combine(ImportRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        File.WriteAllBytes(absolutePath, content);
    }

    private static async Task<LibraryImportStatusDto> ImportAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/library/import", null, Cancel.Token);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<LibraryImportStatusDto>(
            RecommendationApiFixture.Json, Cancel.Token))!;
    }

    private static class ImportableMp3
    {
        public static byte[] Tagged(string title, string artist, string? album) =>
            SyntheticAudio.Mp3(title, artist, album);
    }
}
