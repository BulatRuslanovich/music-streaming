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
    public async Task The_top_tracks_section_answers_404_for_an_unknown_artist()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync();

        var top = await client.GetAsync(
            $"/api/artists/{Guid.CreateVersion7()}/top-tracks", Cancel.Token);

        Assert.Equal(HttpStatusCode.NotFound, top.StatusCode);
    }
}
