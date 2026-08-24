// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class PlaylistSuggestionTests(RecommendationApiFixture fixture)
{
    private const string StrangerUsername = "suggestion-stranger";
    private const string StrangerPassword = "suggestion-stranger-pass";

    [Fact]
    public async Task An_empty_playlist_still_gets_something_to_add()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await fixture.BuildRecommendationsAsync(library.UserId);

        var playlist = await CreatePlaylistAsync(client, "Empty");

        var suggestions = await client.GetFromJsonAsync<List<RecommendedTrackDto>>(
            $"/api/playlists/{playlist}/suggestions?limit=5", Cancel.Token);

        Assert.NotNull(suggestions);
        Assert.NotEmpty(suggestions);
    }

    [Fact]
    public async Task No_suggestion_is_already_in_the_playlist()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await fixture.BuildRecommendationsAsync(library.UserId);

        var playlist = await CreatePlaylistAsync(client, "Seeded");

        var seeded = library.TrackIds.Take(3).ToList();
        foreach (var trackId in seeded)
        {
            var added = await client.PostAsJsonAsync(
                $"/api/playlists/{playlist}/tracks", new { trackId }, Cancel.Token);
            added.EnsureSuccessStatusCode();
        }

        var suggestions = await client.GetFromJsonAsync<List<RecommendedTrackDto>>(
            $"/api/playlists/{playlist}/suggestions?limit=10", Cancel.Token);

        Assert.NotNull(suggestions);
        Assert.All(suggestions, item => Assert.DoesNotContain(item.Track.Id, seeded));
    }

    [Fact]
    public async Task Looking_at_suggestions_does_not_record_impressions()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await fixture.BuildRecommendationsAsync(library.UserId);

        var playlist = await CreatePlaylistAsync(client, "Quiet");

        var before = await CountImpressionsAsync();

        var response = await client.GetAsync(
            $"/api/playlists/{playlist}/suggestions?limit=10", Cancel.Token);
        response.EnsureSuccessStatusCode();

        Assert.Equal(before, await CountImpressionsAsync());
    }

    [Fact]
    public async Task Someone_elses_private_playlist_is_not_found()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, owner) = await fixture.SeedAndSignInAsync();
        var playlist = await CreatePlaylistAsync(owner, "Private");

        var stranger = await fixture.CreateSignedInClientAsync(StrangerUsername, StrangerPassword);

        var response = await stranger.GetAsync(
            $"/api/playlists/{playlist}/suggestions", Cancel.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<Guid> CreatePlaylistAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(
            "/api/playlists", new { name, isPublic = false }, Cancel.Token);

        response.EnsureSuccessStatusCode();

        var playlist = await response.Content.ReadFromJsonAsync<PlaylistDto>(Cancel.Token);
        return playlist!.Id;
    }

    private async Task<int> CountImpressionsAsync()
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.RecommendationImpressions.CountAsync(Cancel.Token);
    }
}
