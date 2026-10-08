// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using App.Dtos;
using App.Recommendations;
using App.Recommendations.Home;
using Infrastructure.Persistence;
using Xunit;

namespace IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class PlaybackSourceTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task Where_a_track_was_started_from_is_stored_with_its_events()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        var session = Guid.NewGuid();

        var response = await client.PostAsJsonAsync("/api/playback/signals", new RecordEventsRequest(
        [
            new("trackStarted", library.Track(0), null, null, 0, 0, 180, session, "home:forYou"),
            new("trackCompleted", library.Track(0), null, null, 180, 180, 180, session, "home:forYou"),
            new("trackSkipped", library.Track(1), null, null, 5, 5, 180, session, "not a/source"),
            new("trackStarted", library.Track(2), null, null, 0, 0, 180, session),
        ]), Cancel.Token);
        response.EnsureSuccessStatusCode();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.PlaybackEvents.AsNoTracking()
            .Where(e => e.SessionId == session)
            .ToDictionaryAsync(e => (e.TrackId, e.Type.ToString()), e => e.Source, Cancel.Token);

        Assert.Equal("home:forYou", stored[(library.Track(0), "TrackStarted")]);
        Assert.Equal("home:forYou", stored[(library.Track(0), "TrackCompleted")]);
        Assert.Null(stored[(library.Track(1), "TrackSkipped")]);
        Assert.Null(stored[(library.Track(2), "TrackStarted")]);
    }

    [Fact]
    public async Task Stats_show_how_each_source_performs()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        var session = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        PlaybackEventRequest Event(string type, int track, int listened, string? source, int secondsAgo = 0) =>
            new(type, track < 0 ? null : library.Track(track), null, now.AddSeconds(-secondsAgo), listened, listened, 200, session, source);

        var response = await client.PostAsJsonAsync("/api/playback/signals", new RecordEventsRequest(
        [
            Event("shelfShown", -1, 0, "home:forYou", 60),
            Event("shelfShown", -1, 0, "home:forYou", 50),
            Event("shelfShown", -1, 0, null, 50),
            Event("trackStarted", 0, 0, "home:forYou", 40),
            Event("trackCompleted", 0, 200, "home:forYou", 30),
            Event("trackLiked", 0, 0, null, 20),
            Event("trackStarted", 1, 0, "home:discover", 15),
            Event("trackSkipped", 1, 8, "home:discover", 10),
            Event("trackStarted", 2, 0, "album", 5),
            Event("trackCompleted", 2, 200, "album", 1),
        ]), Cancel.Token);
        response.EnsureSuccessStatusCode();

        var stats = (await client.GetFromJsonAsync<RecommendationStatsDto>("/api/recommendations/stats?days=7", Cancel.Token))!;
        var forYou = stats.Sources.Single(row => row.Source == "home:forYou");
        var discover = stats.Sources.Single(row => row.Source == "home:discover");
        var album = stats.Sources.Single(row => row.Source == "album");

        Assert.Equal((2, 1, 1, 1), (forYou.Impressions, forYou.Starts, forYou.Completed, forYou.Liked));
        Assert.True(forYou.Recommended);
        Assert.Equal((1, 1, 0), (discover.Skipped, discover.SkippedEarly, discover.Liked));
        Assert.False(album.Recommended);
        Assert.Equal(408, stats.ListenedSeconds);
        Assert.Equal(208, stats.RecommendedSeconds);
    }

    [Fact]
    public async Task Stats_refuse_a_period_beyond_what_is_kept()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync();

        var response = await client.GetAsync("/api/recommendations/stats?days=1000", Cancel.Token);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_start_from_a_recommended_shelf_carries_the_features_it_was_ranked_with()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await fixture.EmbedLibraryAsync();
        await fixture.BuildRecommendationsAsync(library.UserId);

        Guid shelved;
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var shelves = await db.RecommendationCache.AsNoTracking()
                .Where(entry => entry.UserId == library.UserId)
                .ToListAsync(Cancel.Token);

            var item = shelves
                .Where(shelf => PersonalWeights.ShelfServes("home:forYou", shelf.ShelfKey))
                .SelectMany(shelf => shelf.Payload)
                .FirstOrDefault(entry => entry.Features is not null);

            Assert.SkipWhen(item is null, "The seeded library produced no For you shelf.");
            shelved = item!.ItemId;
        }

        var session = Guid.NewGuid();
        var response = await client.PostAsJsonAsync("/api/playback/signals", new RecordEventsRequest(
        [
            new("trackStarted", shelved, null, null, 0, 0, 180, session, "home:forYou"),
            new("trackStarted", shelved, null, null, 0, 0, 180, session, "album"),
        ]), Cancel.Token);
        response.EnsureSuccessStatusCode();

        using var check = fixture.CreateScope();
        var stored = await check.ServiceProvider.GetRequiredService<ApplicationDbContext>().PlaybackEvents.AsNoTracking()
            .Where(e => e.SessionId == session)
            .ToDictionaryAsync(e => e.Source!, e => e.Features, Cancel.Token);

        Assert.Equal(RankingFeatures.Count, stored["home:forYou"]!.Length);
        Assert.Null(stored["album"]);
    }
}

