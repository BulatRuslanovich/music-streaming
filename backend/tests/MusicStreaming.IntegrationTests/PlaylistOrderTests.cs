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
public class PlaylistOrderTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task Tracks_are_appended_in_the_order_they_were_added()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (client, library) = await SetUpAsync();
        var playlist = await CreateAsync(client, "Append order");

        foreach (var trackId in library.TrackIds.Take(4))
            await AddAsync(client, playlist.Id, trackId);

        Assert.Equal(library.TrackIds.Take(4), await OrderOfAsync(client, playlist.Id));
    }

    [Fact]
    public async Task Adding_the_same_track_twice_leaves_one_row()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (client, library) = await SetUpAsync();
        var playlist = await CreateAsync(client, "No duplicates");

        await AddAsync(client, playlist.Id, library.Track(0));
        await AddAsync(client, playlist.Id, library.Track(1));
        await AddAsync(client, playlist.Id, library.Track(0));

        Assert.Equal([library.Track(0), library.Track(1)], await OrderOfAsync(client, playlist.Id));
    }

    [Fact]
    public async Task Removing_a_track_closes_the_gap_in_the_numbering()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (client, library) = await SetUpAsync();
        var playlist = await CreateAsync(client, "Gap closing");

        foreach (var trackId in library.TrackIds.Take(4))
            await AddAsync(client, playlist.Id, trackId);

        var removed = await client.DeleteAsync(
            $"/api/playlists/{playlist.Id}/tracks/{library.Track(1)}", Cancel.Token);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        Assert.Equal(
            [library.Track(0), library.Track(2), library.Track(3)],
            await OrderOfAsync(client, playlist.Id));

        Assert.Equal([0, 1, 2], await PositionsOfAsync(playlist.Id));
    }

    [Fact]
    public async Task Removing_a_track_that_is_not_there_is_not_found()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (client, library) = await SetUpAsync();
        var playlist = await CreateAsync(client, "Missing track");

        await AddAsync(client, playlist.Id, library.Track(0));

        var response = await client.DeleteAsync(
            $"/api/playlists/{playlist.Id}/tracks/{library.Track(3)}", Cancel.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reorder_applies_the_requested_order()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (client, library) = await SetUpAsync();
        var playlist = await CreateAsync(client, "Full reorder");

        foreach (var trackId in library.TrackIds.Take(4))
            await AddAsync(client, playlist.Id, trackId);

        var wanted = new[] { library.Track(3), library.Track(0), library.Track(2), library.Track(1) };
        await ReorderAsync(client, playlist.Id, wanted);

        Assert.Equal(wanted, await OrderOfAsync(client, playlist.Id));
        Assert.Equal([0, 1, 2, 3], await PositionsOfAsync(playlist.Id));
    }

    [Fact]
    public async Task Tracks_left_out_of_the_request_keep_their_order_at_the_end()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (client, library) = await SetUpAsync();
        var playlist = await CreateAsync(client, "Partial reorder");

        foreach (var trackId in library.TrackIds.Take(5))
            await AddAsync(client, playlist.Id, trackId);

        await ReorderAsync(client, playlist.Id, [library.Track(4), library.Track(3)]);

        Assert.Equal(
            [library.Track(4), library.Track(3), library.Track(0), library.Track(1), library.Track(2)],
            await OrderOfAsync(client, playlist.Id));
    }

    [Fact]
    public async Task Unknown_ids_in_the_request_are_ignored()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (client, library) = await SetUpAsync();
        var playlist = await CreateAsync(client, "Stale ids");

        foreach (var trackId in library.TrackIds.Take(3))
            await AddAsync(client, playlist.Id, trackId);

        await ReorderAsync(
            client, playlist.Id, [Guid.CreateVersion7(), library.Track(2), library.Track(0)]);

        Assert.Equal(
            [library.Track(2), library.Track(0), library.Track(1)],
            await OrderOfAsync(client, playlist.Id));
    }

    [Fact]
    public async Task An_empty_reorder_leaves_the_playlist_as_it_was()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (client, library) = await SetUpAsync();
        var playlist = await CreateAsync(client, "Empty reorder");

        foreach (var trackId in library.TrackIds.Take(3))
            await AddAsync(client, playlist.Id, trackId);

        await ReorderAsync(client, playlist.Id, []);

        Assert.Equal(library.TrackIds.Take(3), await OrderOfAsync(client, playlist.Id));
    }

    private async Task<(HttpClient Client, SeededLibrary Library)> SetUpAsync()
    {
        var client = await fixture.CreateSignedInClientAsync();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return (client, await LibrarySeeder.SeedAsync(db));
    }

    private static async Task<PlaylistDto> CreateAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(
            "/api/playlists", new { name, description = (string?)null, isPublic = false }, Cancel.Token);

        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<PlaylistDto>(Cancel.Token);
        Assert.NotNull(created);

        return created;
    }

    private static async Task AddAsync(HttpClient client, Guid playlistId, Guid trackId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/playlists/{playlistId}/tracks", new { trackId }, Cancel.Token);

        response.EnsureSuccessStatusCode();
    }

    private static async Task ReorderAsync(HttpClient client, Guid playlistId, Guid[] trackIds)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/playlists/{playlistId}/tracks/order", new { trackIds }, Cancel.Token);

        response.EnsureSuccessStatusCode();
    }

    private static async Task<IReadOnlyList<Guid>> OrderOfAsync(HttpClient client, Guid playlistId)
    {
        var detail = await client.GetFromJsonAsync<PlaylistDetailDto>(
            $"/api/playlists/{playlistId}", Cancel.Token);

        Assert.NotNull(detail);
        return [.. detail.Tracks.Select(t => t.Id)];
    }

    private async Task<IReadOnlyList<int>> PositionsOfAsync(Guid playlistId)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return [.. db.PlaylistTracks
            .Where(pt => pt.PlaylistId == playlistId)
            .OrderBy(pt => pt.Position)
            .Select(pt => pt.Position)];
    }
}
