// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Net.Http.Json;
using MusicStreaming.Application.Dtos;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class ArtistDetailTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task An_artist_page_lists_that_artists_own_tracks_as_its_top()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await fixture.RefreshSimilarityAsync();

        var top = await client.GetFromJsonAsync<List<TrackDto>>(
            $"/api/artists/{library.Artist(0)}/top-tracks?limit=3", Cancel.Token);

        Assert.NotNull(top);
        Assert.NotEmpty(top);
        Assert.True(top.Count <= 3);
        Assert.All(top, track => Assert.Equal(library.Artist(0), track.ArtistId));
    }

    [Fact]
    public async Task Similar_artists_never_include_the_artist_being_viewed()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await fixture.RefreshSimilarityAsync();

        var similar = await client.GetFromJsonAsync<List<ArtistDto>>(
            $"/api/artists/{library.Artist(0)}/similar?limit=5", Cancel.Token);

        Assert.NotNull(similar);
        Assert.DoesNotContain(similar, artist => artist.Id == library.Artist(0));
    }

    [Fact]
    public async Task Similar_artists_fall_back_when_no_similarity_has_been_computed()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        var response = await client.GetAsync(
            $"/api/artists/{library.Artist(0)}/similar?limit=5", Cancel.Token);

        response.EnsureSuccessStatusCode();

        var similar = await response.Content.ReadFromJsonAsync<List<ArtistDto>>(Cancel.Token);

        Assert.NotNull(similar);
        Assert.NotEmpty(similar);
        Assert.DoesNotContain(similar, artist => artist.Id == library.Artist(0));
    }

    [Fact]
    public async Task Both_artist_sections_answer_404_for_an_unknown_artist()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync();
        var missing = Guid.CreateVersion7();

        var top = await client.GetAsync($"/api/artists/{missing}/top-tracks", Cancel.Token);
        var similar = await client.GetAsync($"/api/artists/{missing}/similar", Cancel.Token);

        Assert.Equal(HttpStatusCode.NotFound, top.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, similar.StatusCode);
    }
}
