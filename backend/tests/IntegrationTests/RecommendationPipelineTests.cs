// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Infrastructure.Persistence;
using Xunit;
using App.Recommendations;
using App.Recommendations.Home;

namespace IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class RecommendationPipelineTests(RecommendationApiFixture fixture)
{
    private static readonly TimeSpan IngestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Reported_events_are_stored_and_shape_the_profile()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        await PostEventsAsync(client,
            Completed(library.Track(0)),
            Liked(library.Track(0)),
            Completed(library.Track(1)),
            Skipped(library.Track(10), listened: 3));

        await WaitForEventsAsync(4);
        await RollupAsync(library.UserId);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var profile = await db.UserTasteProfiles.AsNoTracking()
            .FirstAsync(p => p.UserId == library.UserId, Cancel.Token);

        Assert.Equal(3, profile.PositiveSignalMass, precision: 3);

        var loved = await db.UserTrackAffinities.AsNoTracking()
            .FirstAsync(a => a.UserId == library.UserId && a.TrackId == library.Track(0), Cancel.Token);

        var rejected = await db.UserTrackAffinities.AsNoTracking()
            .FirstAsync(a => a.UserId == library.UserId && a.TrackId == library.Track(10), Cancel.Token);

        // Score = w / (|w| + 3), поэтому вес больше 2 — это счёт выше 0.4.
        Assert.True(loved.DecayedWeight > 2, $"A completed and liked track weighs {loved.DecayedWeight}");
        Assert.True(rejected.DecayedWeight < 0, $"An abandoned track weighs {rejected.DecayedWeight}");
        Assert.Equal(1, rejected.SkipCount);
    }

    [Fact]
    public async Task Rolling_up_twice_does_not_double_count()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        await PostEventsAsync(client, Completed(library.Track(0)), Completed(library.Track(1)));
        await WaitForEventsAsync(2);

        await RollupAsync(library.UserId);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var first = await db.UserTrackAffinities.AsNoTracking()
            .FirstAsync(a => a.TrackId == library.Track(0), Cancel.Token);

        await RollupAsync(library.UserId);

        var second = await db.UserTrackAffinities.AsNoTracking()
            .FirstAsync(a => a.TrackId == library.Track(0), Cancel.Token);

        Assert.Equal(first.PlayCount, second.PlayCount);
        Assert.Equal(first.DecayedWeight, second.DecayedWeight, precision: 10);
        Assert.Equal(first.CompletionSamples, second.CompletionSamples);
    }

    [Fact]
    public async Task A_listening_history_produces_personalised_shelves()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        await PostEventsAsync(client,
            Completed(library.Track(0)),
            Completed(library.Track(1)),
            Completed(library.Track(2)),
            Liked(library.Track(1)),
            Skipped(library.Track(15), listened: 2),
            Skipped(library.Track(16), listened: 3));

        await WaitForEventsAsync(6);
        await fixture.BuildRecommendationsAsync(library.UserId);

        var home = await fixture.HomeAsync(library.UserId, 12);

        Assert.NotNull(home);
        Assert.NotEmpty(home);

        Assert.All(home, section =>
        {
            var count = (section.Tracks?.Count ?? 0) + (section.Artists?.Count ?? 0) + (section.Albums?.Count ?? 0);
            Assert.True(count > 0, $"Shelf {section.Key} came back empty");
            Assert.False(string.IsNullOrWhiteSpace(section.BaseKey));
        });

        var recommended = home
            .Where(s => s.Tracks is not null)
            .SelectMany(s => s.Tracks!)
            .ToList();

        Assert.NotEmpty(recommended);
        Assert.All(recommended, item => Assert.False(string.IsNullOrWhiteSpace(item.Reason.Kind)));

        Assert.All(recommended, item => Assert.Null(item.Score));

        var forYou = home.FirstOrDefault(s => s.BaseKey == ShelfKeys.ForYou);
        Assert.NotNull(forYou);
        Assert.NotNull(forYou.Tracks);

        var ordered = forYou.Tracks!.Select(item => item.Track).ToList();

        var firstPreferred = ordered.FindIndex(track => track.ArtistId == library.Artist(0));
        var firstRejected = ordered.FindIndex(
            track => track.Id == library.Track(15) || track.Id == library.Track(16));

        Assert.True(firstPreferred >= 0, "Nothing from the artist the listener played made the shelf");
        Assert.True(
            firstRejected < 0 || firstPreferred < firstRejected,
            "An abandoned track outranked the artist the listener actually played");
    }

    [Fact]
    public async Task No_single_artist_dominates_a_shelf()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync(artistCount: 14, tracksPerArtist: 4);

        var events = Enumerable.Range(0, 5).Select(index => Completed(library.Track(index))).ToArray();
        await PostEventsAsync(client, events);
        await WaitForEventsAsync(events.Length);
        await fixture.BuildRecommendationsAsync(library.UserId);

        var home = await fixture.HomeAsync(library.UserId, 12);

        var forYou = home!.First(s => s.BaseKey == ShelfKeys.ForYou);

        var perArtist = forYou.Tracks!
            .GroupBy(item => item.Track.ArtistId)
            .Select(group => group.Count())
            .Max();

        var breakdown = string.Join(", ", forYou.Tracks!
            .GroupBy(item => item.Track.ArtistName)
            .Select(group => $"{group.Key}={group.Count()}"));

        Assert.True(
            perArtist <= RecommendationTuning.Diversity.MaxPerArtist,
            $"One artist took {perArtist} of {forYou.Tracks!.Count} slots ({breakdown})");
    }

    [Fact]
    public async Task A_user_with_no_history_still_gets_a_home_page()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await fixture.BuildRecommendationsAsync(library.UserId);

        var home = await fixture.HomeAsync(library.UserId, 12);

        Assert.NotNull(home);
        Assert.NotEmpty(home);

        Assert.Contains(
            home,
            section => section.BaseKey is ShelfKeys.ForYou or ShelfKeys.Discover);
    }

    [Fact]
    public async Task A_user_with_a_single_play_gets_recommendations()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        await PostEventsAsync(client, Completed(library.Track(0)));
        await WaitForEventsAsync(1);
        await fixture.BuildRecommendationsAsync(library.UserId);

        var home = await fixture.HomeAsync(library.UserId, 12);

        Assert.NotNull(home);
        Assert.NotEmpty(home);

        var tracks = home.Where(s => s.Tracks is not null).SelectMany(s => s.Tracks!).ToList();
        Assert.NotEmpty(tracks);
    }

    [Fact]
    public async Task Only_skipping_does_not_break_the_page()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        var events = Enumerable.Range(0, 12)
            .Select(index => Skipped(library.Track(index), listened: 2))
            .ToArray();

        await PostEventsAsync(client, events);
        await WaitForEventsAsync(events.Length);
        await fixture.BuildRecommendationsAsync(library.UserId);

        var home = await fixture.HomeAsync(library.UserId, 12);
        Assert.NotNull(home);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var negative = await db.UserTrackAffinities.AsNoTracking()
            .CountAsync(a => a.UserId == library.UserId && a.DecayedWeight < 0, Cancel.Token);

        Assert.Equal(12, negative);
    }


    private static object Completed(Guid trackId) => new
    {
        type = "trackCompleted",
        trackId,
        occurredAt = DateTimeOffset.UtcNow,
        positionSeconds = 200,
        listenedSeconds = 200,
        durationSeconds = 200,
        sessionId = Session,
    };

    private static object Skipped(Guid trackId, int listened) => new
    {
        type = "trackSkipped",
        trackId,
        occurredAt = DateTimeOffset.UtcNow,
        positionSeconds = listened,
        listenedSeconds = listened,
        durationSeconds = 200,
        sessionId = Session,
    };

    private static object Liked(Guid trackId) => new
    {
        type = "trackLiked",
        trackId,
        occurredAt = DateTimeOffset.UtcNow,
        sessionId = Session,
    };

    private static readonly Guid Session = Guid.CreateVersion7();

    private static async Task PostEventsAsync(HttpClient client, params object[] events)
    {
        var response = await client.PostAsJsonAsync("/api/playback/signals", new { events });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private async Task WaitForEventsAsync(int expected)
    {
        var deadline = DateTimeOffset.UtcNow + IngestTimeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            using var scope = fixture.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            if (await db.PlaybackEvents.CountAsync() >= expected)
                return;

            await Task.Delay(100);
        }

        Assert.Fail($"Only {await CountEventsAsync()} of {expected} events were written within {IngestTimeout}.");
    }

    private async Task<int> CountEventsAsync()
    {
        using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .PlaybackEvents.CountAsync();
    }

    private async Task RollupAsync(Guid userId)
    {
        using var scope = fixture.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ProfileRollupService>().RollupAsync(userId);
    }

}
