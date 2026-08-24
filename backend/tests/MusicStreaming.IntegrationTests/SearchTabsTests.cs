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
public class SearchTabsTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task The_in_memory_rank_agrees_with_the_database_function()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        await fixture.SeedAndSignInAsync();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        (string Value, string Term)[] cases =
        [
            ("nirvana", "nirvana"),
            ("nirvana unplugged", "nirvana"),
            ("the nirvana story", "nirvana"),
            ("supernirvana", "nirvana"),
            ("pearl jam", "nirvana"),
            ("a b c", "b"),
            ("abc", "b"),
        ];

        foreach (var (value, term) in cases)
        {
            var fromDatabase = await db.Database
                .SqlQuery<int>($"SELECT search_rank({value}, {term}) AS \"Value\"")
                .SingleAsync(Cancel.Token);

            Assert.Equal(fromDatabase, SearchRank.Evaluate(value, term));
        }
    }

    [Fact]
    public async Task The_top_result_is_the_strongest_match_across_every_type()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync();

        var results = await client.GetFromJsonAsync<SearchResultDto>(
            "/api/search?q=Artist%200&limit=20", RecommendationApiFixture.Json, Cancel.Token);

        Assert.NotNull(results);
        Assert.NotNull(results.Top);
        Assert.Equal(SearchResultKind.Artist, results.Top.Kind);
        Assert.NotNull(results.Top.Artist);
        Assert.Equal("Artist 0", results.Top.Artist.Name);
    }

    [Fact]
    public async Task A_query_that_matches_nothing_has_no_top_result()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync();

        var results = await client.GetFromJsonAsync<SearchResultDto>(
            "/api/search?q=zzzznothingmatchesthis", RecommendationApiFixture.Json, Cancel.Token);

        Assert.NotNull(results);
        Assert.Null(results.Top);
        Assert.Empty(results.Tracks);
    }

    [Fact]
    public async Task Each_tab_pages_independently_and_agrees_with_the_combined_search()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync();

        var combined = await client.GetFromJsonAsync<SearchResultDto>(
            "/api/search?q=Track&limit=50", RecommendationApiFixture.Json, Cancel.Token);

        var firstPage = await client.GetFromJsonAsync<PagedResult<TrackDto>>(
            "/api/search/tracks?q=Track&page=1&pageSize=3", Cancel.Token);

        var secondPage = await client.GetFromJsonAsync<PagedResult<TrackDto>>(
            "/api/search/tracks?q=Track&page=2&pageSize=3", Cancel.Token);

        Assert.NotNull(combined);
        Assert.NotNull(firstPage);
        Assert.NotNull(secondPage);

        Assert.Equal(3, firstPage.Items.Count);
        Assert.Equal(combined.Tracks.Count, firstPage.Total);

        Assert.Equal(
            combined.Tracks.Take(6).Select(t => t.Id),
            firstPage.Items.Concat(secondPage.Items).Select(t => t.Id));
    }

    [Fact]
    public async Task An_empty_query_gives_an_empty_page_rather_than_everything()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync();

        var page = await client.GetFromJsonAsync<PagedResult<TrackDto>>(
            "/api/search/tracks?q=&page=1&pageSize=10", Cancel.Token);

        Assert.NotNull(page);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
    }
}
