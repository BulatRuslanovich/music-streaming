// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;
using MusicStreaming.Application.Services.Recommendations;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class HomeFeedTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task The_feed_requires_a_session()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var response = await fixture.CreateClient().GetAsync("/api/home/feed", Cancel.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_empty_library_yields_an_empty_feed()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();

        using (var scope = fixture.CreateScope())
            await LibrarySeeder.ClearAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());

        var feed = await GetAsync(client);

        Assert.Empty(feed.Blocks);
    }

    [Fact]
    public async Task Every_block_carries_a_unique_key_and_something_to_show()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await fixture.BuildRecommendationsAsync(library.UserId);

        var feed = await GetAsync(client);

        Assert.NotEmpty(feed.Blocks);
        Assert.Distinct(feed.Blocks.Select(block => block.Key));

        foreach (var block in feed.Blocks)
            Assert.True(Counted(block) > 0, $"Block \"{block.Key}\" arrived empty");
    }

    [Fact]
    public async Task The_feed_leads_with_a_hero_and_mixes_up_the_layouts()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await fixture.BuildRecommendationsAsync(library.UserId);

        var feed = await GetAsync(client);

        Assert.Equal(HomeBlockLayout.Hero, feed.Blocks[0].Layout);
        Assert.Equal(HomeBlockKeys.DailyMix, feed.Blocks[0].BaseKey);

        Assert.True(
            feed.Blocks.Select(block => block.Layout).Distinct().Count() > 1,
            "The whole feed came back as a single layout");
    }

    [Fact]
    public async Task Every_block_lands_in_a_zone_and_the_zones_never_interleave()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await fixture.BuildRecommendationsAsync(library.UserId);

        var feed = await GetAsync(client);

        Assert.Equal(HomeZone.Lead, feed.Blocks[0].Zone);

        var zones = feed.Blocks.Select(block => block.Zone).ToList();
        Assert.Equal(zones.Order(), zones);
    }

    [Fact]
    public async Task The_quick_zone_stays_a_single_row()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await fixture.BuildRecommendationsAsync(library.UserId);

        foreach (var index in new[] { 0, 1, 2 })
        {
            var response = await client.PostAsync($"/api/tracks/{library.Track(index)}/favorite", null, Cancel.Token);
            response.EnsureSuccessStatusCode();
        }

        var feed = await GetAsync(client);

        var shortcuts = feed.Blocks
            .Where(block => block.Zone == HomeZone.Quick)
            .Sum(block => block.Layout == HomeBlockLayout.Tile ? 1 : Counted(block));

        Assert.True(shortcuts <= 8, $"{shortcuts} shortcuts reached the quick zone");
    }

    [Fact]
    public async Task Recommendations_are_trimmed_and_never_repeat_the_pages_own_blocks()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync(artistCount: 10, tracksPerArtist: 4);
        await fixture.BuildRecommendationsAsync(library.UserId);

        var feed = await GetAsync(client);

        var shelves = feed.Blocks
            .Where(block => block.Layout == HomeBlockLayout.Shelf && block.Reason is not null)
            .ToList();

        Assert.True(shelves.Count <= 2, $"{shelves.Count} recommendation shelves reached the feed");
        Assert.Distinct(shelves.Select(block => block.BaseKey));

        foreach (var duplicated in new[] { ShelfKeys.Popular, ShelfKeys.NewReleases, ShelfKeys.ContinueListening })
            Assert.DoesNotContain(feed.Blocks, block => block.BaseKey == duplicated);
    }

    [Fact]
    public async Task The_mix_of_the_day_holds_still_while_the_day_does()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await fixture.BuildRecommendationsAsync(library.UserId);

        var first = Hero(await GetAsync(client));
        var second = Hero(await GetAsync(client));

        Assert.NotNull(first);
        Assert.Equal(first.Tracks!.Select(track => track.Id), second!.Tracks!.Select(track => track.Id));
    }

    [Fact]
    public async Task The_mix_of_the_day_survives_a_rebuild_of_the_recommendations()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync(artistCount: 12, tracksPerArtist: 5);
        await fixture.BuildRecommendationsAsync(library.UserId);

        var first = Hero(await GetAsync(client));
        Assert.NotNull(first);

        // Пул под миксом переписывается: воркер рекомендаций работает несколько раз в сутки.
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.RecommendationCache.Where(entry => entry.UserId == library.UserId).ExecuteDeleteAsync();
        }

        var second = Hero(await GetAsync(client));

        Assert.NotNull(second);
        Assert.Equal(first.Tracks!.Select(track => track.Id), second.Tracks!.Select(track => track.Id));
    }

    [Fact]
    public async Task The_mix_of_the_day_is_served_from_the_snapshot_it_was_stored_as()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync(artistCount: 12, tracksPerArtist: 5);
        await fixture.BuildRecommendationsAsync(library.UserId);

        var hero = Hero(await GetAsync(client));
        Assert.NotNull(hero);

        IReadOnlyList<Guid> trimmed;

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var snapshot = await db.DailyMixes.SingleAsync(mix => mix.UserId == library.UserId);

            // Геро несёт превью снимка, а не весь снимок — сверяем по началу списка.
            Assert.Equal(
                hero.Tracks!.Select(track => track.Id),
                snapshot.TrackIds.Take(HomeBlocks.HeroTracks));

            trimmed = [.. snapshot.TrackIds.Reverse().Take(6)];
            snapshot.TrackIds = trimmed;
            await db.SaveChangesAsync();
        }

        var again = Hero(await GetAsync(client));

        Assert.NotNull(again);
        Assert.Equal(trimmed, again.Tracks!.Select(track => track.Id));
    }

    [Fact]
    public async Task The_hero_previews_the_mix_of_the_day_and_still_says_how_long_it_is()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync(artistCount: 20, tracksPerArtist: 6);
        await fixture.BuildRecommendationsAsync(library.UserId);

        var hero = Hero(await GetAsync(client));

        Assert.NotNull(hero);
        Assert.Equal(HomeBlocks.HeroTracks, hero.Tracks!.Count);
        Assert.Equal(60, hero.TotalCount);
        Assert.Distinct(hero.Tracks.Select(track => track.Id));
    }

    [Fact]
    public async Task A_listener_without_history_still_gets_a_mix_of_the_day()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync();

        var feed = await GetAsync(client);

        Assert.True(feed.IsColdStart);
        Assert.NotNull(Hero(feed));
    }

    [Fact]
    public async Task Liked_tracks_arrive_as_a_tile_that_knows_the_whole_count()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        foreach (var index in new[] { 0, 1, 2, 3, 4, 5 })
        {
            var response = await client.PostAsync($"/api/tracks/{library.Track(index)}/favorite", null, Cancel.Token);
            response.EnsureSuccessStatusCode();
        }

        var feed = await GetAsync(client);
        var tile = feed.Blocks.Single(block => block.BaseKey == HomeBlockKeys.Favorites);

        Assert.Equal(HomeBlockLayout.Tile, tile.Layout);
        Assert.Equal(6, tile.TotalCount);
        Assert.Equal(4, tile.Tracks!.Count);
    }

    [Fact]
    public async Task Fresh_arrivals_open_as_their_own_mix_newest_first()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        var mix = await GetMixAsync(client, "new");

        Assert.Equal(HomeMixKind.New, mix.Kind);
        Assert.Equal(library.Track(0), mix.Tracks[0].Id);
        Assert.True(mix.Tracks.Count <= 20, $"{mix.Tracks.Count} tracks came back");

        var added = mix.Tracks.Select(track => track.CreatedAt).ToList();
        Assert.Equal(added.OrderByDescending(at => at), added);
    }

    [Fact]
    public async Task The_mix_of_the_day_page_matches_the_hero_on_the_feed()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await fixture.BuildRecommendationsAsync(library.UserId);

        var hero = Hero(await GetAsync(client));
        var mix = await GetMixAsync(client, "daily");

        Assert.NotNull(hero);
        Assert.Equal(HomeMixKind.Daily, mix.Kind);
        Assert.Equal(
            hero.Tracks!.Select(track => track.Id),
            mix.Tracks.Take(HomeBlocks.HeroTracks).Select(track => track.Id));
        Assert.Equal(mix.Tracks.Count, hero.TotalCount);
    }

    [Fact]
    public async Task An_unknown_mix_is_refused()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync();

        var response = await client.GetAsync("/api/home/mixes/whatever", Cancel.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<HomeMixDto> GetMixAsync(HttpClient client, string kind) =>
        (await client.GetFromJsonAsync<HomeMixDto>(
            $"/api/home/mixes/{kind}", RecommendationApiFixture.Json, Cancel.Token))!;

    private static HomeBlockDto? Hero(HomeFeedDto feed) =>
        feed.Blocks.FirstOrDefault(block => block.BaseKey == HomeBlockKeys.DailyMix);

    private static int Counted(HomeBlockDto block) =>
        (block.Tracks?.Count ?? 0)
        + (block.Albums?.Count ?? 0)
        + (block.Artists?.Count ?? 0)
        + (block.Playlists?.Count ?? 0);

    private static async Task<HomeFeedDto> GetAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<HomeFeedDto>(
            "/api/home/feed", RecommendationApiFixture.Json, Cancel.Token))!;

}
