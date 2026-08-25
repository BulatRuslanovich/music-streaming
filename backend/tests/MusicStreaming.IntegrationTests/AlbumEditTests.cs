// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class AlbumEditTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task An_album_can_be_retitled_and_redated()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        var album = library.AlbumIds[0];

        var updated = await UpdateAsync(client, album, new { title = "Retitled Album", year = 1987 });

        Assert.Equal("Retitled Album", updated.Title);
        Assert.Equal(1987, updated.Year);

        var reloaded = await client.GetFromJsonAsync<AlbumDetailDto>(
            $"/api/albums/{album}", RecommendationApiFixture.Json, Cancel.Token);

        Assert.Equal("Retitled Album", reloaded!.Title);
        Assert.Equal(1987, reloaded.Year);
    }

    [Fact]
    public async Task Moving_an_album_to_another_artist_reparents_it_without_touching_its_tracks()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        var album = library.AlbumIds[0];

        var before = await client.GetFromJsonAsync<AlbumDetailDto>(
            $"/api/albums/{album}", RecommendationApiFixture.Json, Cancel.Token);

        var updated = await UpdateAsync(client, album, new { artist = "Album Artist Of Record" });

        Assert.Equal("Album Artist Of Record", updated.ArtistName);
        Assert.NotEqual(before!.ArtistId, updated.ArtistId);

        var after = await client.GetFromJsonAsync<AlbumDetailDto>(
            $"/api/albums/{album}", RecommendationApiFixture.Json, Cancel.Token);

        Assert.Equal(before.Tracks.Count, after!.Tracks.Count);
        Assert.All(after.Tracks, track => Assert.Equal(before.ArtistId, track.ArtistId));
    }

    [Fact]
    public async Task Retitling_an_album_onto_one_that_the_artist_already_has_is_a_conflict()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var first = db.Albums.Find(library.AlbumIds[0])!;
        var second = db.Albums.Find(library.AlbumIds[1])!;

        // Ставим второй альбом тому же артисту, чтобы столкнуть его с названием первого.
        second.ArtistId = first.ArtistId;
        await db.SaveChangesAsync(Cancel.Token);

        var response = await client.PutAsJsonAsync(
            $"/api/albums/{second.Id}", new { title = first.Title }, Cancel.Token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task An_empty_title_and_an_impossible_year_are_both_refused()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        var album = library.AlbumIds[0];

        var blank = await client.PutAsJsonAsync($"/api/albums/{album}", new { title = "   " }, Cancel.Token);
        var ancient = await client.PutAsJsonAsync($"/api/albums/{album}", new { year = 42 }, Cancel.Token);

        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, ancient.StatusCode);
    }

    [Fact]
    public async Task Editing_an_unknown_album_answers_404()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/albums/{Guid.CreateVersion7()}", new { title = "Nowhere" }, Cancel.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_cover_can_be_uploaded_served_in_both_sizes_and_removed_again()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        var album = library.AlbumIds[0];

        using var form = new MultipartFormDataContent();
        using var image = new ByteArrayContent(TestImage.Png());
        image.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(image, "file", "cover.png");

        var uploaded = await client.PostAsync($"/api/albums/{album}/cover", form, Cancel.Token);
        uploaded.EnsureSuccessStatusCode();

        var dto = (await uploaded.Content.ReadFromJsonAsync<AlbumDto>(
            RecommendationApiFixture.Json, Cancel.Token))!;
        Assert.True(dto.HasCover);

        foreach (var size in new[] { "Full", "Thumb" })
        {
            var cover = await client.GetAsync($"/api/albums/{album}/cover?size={size}", Cancel.Token);
            cover.EnsureSuccessStatusCode();
            Assert.True((await cover.Content.ReadAsByteArrayAsync(Cancel.Token)).Length > 0);
        }

        var removed = await client.DeleteAsync($"/api/albums/{album}/cover", Cancel.Token);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        var gone = await client.GetAsync($"/api/albums/{album}/cover", Cancel.Token);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task A_listener_may_read_an_album_but_never_change_it()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, _) = await fixture.SeedAndSignInAsync();
        var listener = await fixture.CreateSignedInClientAsync("album-listener", "listener-password-1");
        var album = library.AlbumIds[0];

        var read = await listener.GetAsync($"/api/albums/{album}", Cancel.Token);
        var write = await listener.PutAsJsonAsync(
            $"/api/albums/{album}", new { title = "Not Allowed" }, Cancel.Token);
        var removeCover = await listener.DeleteAsync($"/api/albums/{album}/cover", Cancel.Token);

        read.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, removeCover.StatusCode);
    }

    private static async Task<AlbumDto> UpdateAsync(HttpClient client, Guid album, object body)
    {
        var response = await client.PutAsJsonAsync($"/api/albums/{album}", body, Cancel.Token);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AlbumDto>(
            RecommendationApiFixture.Json, Cancel.Token))!;
    }
}
