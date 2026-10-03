// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using App.Dtos;
using Domain.Entities.Recommendations;
using Infrastructure.Persistence;
using Xunit;

namespace IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class TopTracksTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task Plays_shorter_than_thirty_seconds_are_not_counted()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        var now = DateTimeOffset.UtcNow;

        await RecordAsync(library,
            Event(library.Track(0), PlaybackEventType.TrackSkipped, now.AddMinutes(-2), 29),
            Event(library.Track(1), PlaybackEventType.TrackSkipped, now.AddMinutes(-1), 30));

        Assert.Equal([library.Track(1)], await TopAsync(client));
    }

    [Fact]
    public async Task Heartbeats_do_not_count_the_same_listen_twice()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        var now = DateTimeOffset.UtcNow;

        await RecordAsync(library,
            Event(library.Track(0), PlaybackEventType.TrackPlayed, now.AddMinutes(-5), 60),
            Event(library.Track(0), PlaybackEventType.TrackPlayed, now.AddMinutes(-4), 120),
            Event(library.Track(0), PlaybackEventType.TrackSkipped, now.AddMinutes(-3), 130),
            Event(library.Track(1), PlaybackEventType.TrackCompleted, now.AddMinutes(-1), 150));

        Assert.Equal([library.Track(1), library.Track(0)], await TopAsync(client));
    }

    [Fact]
    public async Task The_weekly_top_ranks_by_listening_time_and_leaves_out_older_weeks()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        var now = DateTimeOffset.UtcNow;

        await RecordAsync(library,
            Event(library.Track(0), PlaybackEventType.TrackCompleted, now.AddDays(-1), 100),
            Event(library.Track(1), PlaybackEventType.TrackCompleted, now.AddDays(-2), 150),
            Event(library.Track(1), PlaybackEventType.TrackCompleted, now.AddDays(-2).AddHours(1), 150),
            Event(library.Track(1), PlaybackEventType.TrackSkipped, now.AddDays(-2).AddHours(2), 100),
            Event(library.Track(2), PlaybackEventType.TrackCompleted, now.AddDays(-10), 9000));

        Assert.Equal([library.Track(1), library.Track(0)], await TopAsync(client));
    }

    private static PlaybackEvent Event(Guid trackId, PlaybackEventType type, DateTimeOffset at, int listened) => new()
    {
        TrackId = trackId,
        Type = type,
        OccurredAt = at,
        PositionSeconds = listened,
        ListenedSeconds = listened,
        DurationSeconds = 180,
        SessionId = Guid.CreateVersion7(),
    };

    private async Task RecordAsync(SeededLibrary library, params PlaybackEvent[] events)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        foreach (var playbackEvent in events)
            playbackEvent.UserId = library.UserId;

        db.PlaybackEvents.AddRange(events);
        await db.SaveChangesAsync(Cancel.Token);
    }

    private static async Task<IReadOnlyList<Guid>> TopAsync(HttpClient client)
    {
        var mix = await client.GetFromJsonAsync<HomeMixDto>(
            "/api/home/mixes/top", RecommendationApiFixture.Json, Cancel.Token);

        return [.. mix!.Tracks.Select(track => track.Id)];
    }
}
