// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class TrackUploaderTests(RecommendationApiFixture fixture)
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
        Assert.Equal(await OwnerIdAsync(), await AddedByAsync(uploaded.Id));
    }

    [Fact]
    public async Task A_track_written_without_an_upload_still_shows_up_in_the_uploads()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        // Сид пишет треки напрямую в базу, минуя загрузку, поэтому автора у них нет.
        var uploads = await client.GetFromJsonAsync<PagedResult<AdminUploadDto>>(
            "/api/admin/statistics/uploads?pageSize=200",
            RecommendationApiFixture.Json,
            Cancel.Token);

        var row = Assert.Single(uploads!.Items, u => u.TrackId == library.Track(0));

        Assert.Null(row.AddedByUserId);
        Assert.Null(row.AddedByUsername);
    }

    private async Task<Guid?> AddedByAsync(Guid trackId)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.Tracks.AsNoTracking()
            .Where(t => t.Id == trackId)
            .Select(t => t.AddedByUserId)
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
}
