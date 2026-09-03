// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class TrackIngestionOriginTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task A_file_sent_through_the_browser_is_signed_with_the_person_who_sent_it()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = TrackUploadTestClient.UniqueName("Signed");

        var result = await TrackUploadTestClient.UploadOneAsync(
            client,
            new TestUploadFile(
                $"{name}.mp3",
                "audio/mpeg",
                SyntheticAudio.Mp3($"{name} Title", $"{name} Artist", $"{name} Album")),
            RecommendationApiFixture.Json);

        var uploaded = Assert.Single(result.Uploaded);
        var (addedBy, source) = await OriginAsync(uploaded.Id);

        Assert.Equal(await OwnerIdAsync(), addedBy);
        Assert.Equal(IngestionSource.WebUpload, source);
    }

    [Fact]
    public async Task A_file_picked_up_from_the_drop_folder_is_left_unsigned()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = TrackUploadTestClient.UniqueName("Dropped");

        Drop($"{name}.mp3", SyntheticAudio.Mp3($"{name} Title", $"{name} Artist"));

        var status = await ImportAsync(client);
        Assert.Equal(1, status.Imported);

        var (addedBy, source) = await OriginAsync(await TrackIdAsync($"{name} Title"));

        // Администратор, нажавший «сканировать», не автор того, что лежало в папке.
        Assert.Null(addedBy);
        Assert.Equal(IngestionSource.DirectoryImport, source);
    }

    [Fact]
    public async Task A_track_from_before_the_signature_existed_still_shows_up_in_the_uploads()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        // Сид пишет треки напрямую в базу, минуя pipeline — ровно так же выглядят записи,
        // созданные до появления столбца: Unknown и без пользователя.
        var uploads = await client.GetFromJsonAsync<PagedResult<AdminUploadDto>>(
            "/api/admin/statistics/uploads?pageSize=200",
            RecommendationApiFixture.Json,
            Cancel.Token);

        var row = Assert.Single(uploads!.Items, u => u.TrackId == library.Track(0));

        Assert.Null(row.AddedByUserId);
        Assert.Null(row.AddedByUsername);
        Assert.Equal(IngestionSource.Unknown, row.IngestionSource);
    }

    [Fact]
    public async Task The_uploads_list_can_be_narrowed_to_one_way_of_getting_in()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = TrackUploadTestClient.UniqueName("Filtered");

        await TrackUploadTestClient.UploadOneAsync(
            client,
            new TestUploadFile(
                $"{name}.mp3",
                "audio/mpeg",
                SyntheticAudio.Mp3($"{name} Title", $"{name} Artist")),
            RecommendationApiFixture.Json);

        var web = await UploadsAsync(client, "source=WebUpload");
        var imported = await UploadsAsync(client, "source=DirectoryImport");

        Assert.Contains(web.Items, u => u.Title == $"{name} Title");
        Assert.All(web.Items, u => Assert.Equal(IngestionSource.WebUpload, u.IngestionSource));
        Assert.DoesNotContain(imported.Items, u => u.Title == $"{name} Title");
    }

    private static async Task<PagedResult<AdminUploadDto>> UploadsAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<PagedResult<AdminUploadDto>>(
            $"/api/admin/statistics/uploads?pageSize=200&{query}",
            RecommendationApiFixture.Json,
            Cancel.Token))!;

    private async Task<(Guid? AddedBy, IngestionSource Source)> OriginAsync(Guid trackId)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var track = await db.Tracks.AsNoTracking()
            .Where(t => t.Id == trackId)
            .Select(t => new { t.AddedByUserId, t.IngestionSource })
            .SingleAsync();

        return (track.AddedByUserId, track.IngestionSource);
    }

    private async Task<Guid> TrackIdAsync(string title)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.Tracks.AsNoTracking()
            .Where(t => t.Title == title)
            .Select(t => t.Id)
            .SingleAsync();
    }

    private async Task<Guid> OwnerIdAsync()
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.Users.AsNoTracking()
            .Where(u => u.Username == RecommendationApiFixture.OwnerUsername)
            .Select(u => u.Id)
            .SingleAsync();
    }

    private void Drop(string relativePath, byte[] content)
    {
        var absolutePath = Path.Combine(
            fixture.StoragePath, "import", relativePath.Replace('/', Path.DirectorySeparatorChar));

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
}
