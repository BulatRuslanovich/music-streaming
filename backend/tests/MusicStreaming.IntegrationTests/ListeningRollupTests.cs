// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services.Recommendations;
using MusicStreaming.Domain.Entities;
using MusicStreaming.Domain.Entities.Recommendations;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class ListeningRollupTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task The_rollup_counts_listening_from_the_same_events_as_the_taste_profile()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, _) = await fixture.SeedAndSignInAsync();
        var now = DateTimeOffset.UtcNow;

        await RollupAsync(library,
            Event(library, PlaybackEventType.TrackStarted, now.AddSeconds(-180), 0, 0),
            Event(library, PlaybackEventType.TrackPlayed, now.AddSeconds(-120), 60, 60),
            Event(library, PlaybackEventType.TrackPlayed, now.AddSeconds(-60), 120, 120),
            Event(library, PlaybackEventType.TrackCompleted, now, 180, 180));

        var (seconds, plays) = await TotalsAsync(library.UserId);

        Assert.Equal(180, seconds);
        Assert.Equal(1, plays);

        using var check = fixture.CreateScope();
        var context = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var affinity = context.UserTrackAffinities.Single(
            a => a.UserId == library.UserId && a.TrackId == library.Track(0));

        Assert.Equal(affinity.PlayCount, plays);
    }

    [Fact]
    public async Task Plays_shorter_than_thirty_seconds_are_not_counted()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, _) = await fixture.SeedAndSignInAsync();
        var now = DateTimeOffset.UtcNow;

        await RollupAsync(library,
            Event(library, PlaybackEventType.TrackSkipped, now.AddMinutes(-2), 0, 0),
            Event(library, PlaybackEventType.TrackSkipped, now.AddMinutes(-1), 29, 29),
            Event(library, PlaybackEventType.TrackSkipped, now, 30, 30));

        Assert.Equal((30, 1), await TotalsAsync(library.UserId));
    }

    [Fact]
    public async Task Running_the_rollup_twice_does_not_double_the_numbers()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, _) = await fixture.SeedAndSignInAsync();
        var completed = Event(library, PlaybackEventType.TrackCompleted, DateTimeOffset.UtcNow, 180, 180);

        await RollupAsync(library, completed);
        await RollupAsync(library);

        Assert.Equal(180, (await TotalsAsync(library.UserId)).Seconds);
    }

    [Fact]
    public async Task The_weekly_top_ranks_by_listening_time_and_leaves_out_older_weeks()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        var now = DateTimeOffset.UtcNow;

        await RecordAsync(library.UserId,
            (library.Track(0), now.AddDays(-1), 1, 100),
            (library.Track(1), now.AddDays(-2), 3, 400),
            (library.Track(2), now.AddDays(-10), 9, 9000));

        var mix = (await client.GetFromJsonAsync<HomeMixDto>(
            "/api/home/mixes/top", RecommendationApiFixture.Json, Cancel.Token))!;

        Assert.Equal([library.Track(1), library.Track(0)], mix.Tracks.Select(track => track.Id));
    }

    private static PlaybackEvent Event(
        SeededLibrary library, PlaybackEventType type, DateTimeOffset at, int position, int listened) => new()
        {
            UserId = library.UserId,
            TrackId = library.Track(0),
            Type = type,
            OccurredAt = at,
            PositionSeconds = position,
            ListenedSeconds = listened,
            DurationSeconds = 180,
            SessionId = Guid.CreateVersion7(),
        };

    private async Task RollupAsync(SeededLibrary library, params PlaybackEvent[] events)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.PlaybackEvents.AddRange(events);
        await db.SaveChangesAsync(Cancel.Token);

        await scope.ServiceProvider.GetRequiredService<ProfileRollupService>()
            .RollupAsync(library.UserId, Cancel.Token);
    }

    private async Task<(long Seconds, int Plays)> TotalsAsync(Guid userId)
    {
        using var scope = fixture.CreateScope();
        var stats = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .ListeningStats.AsNoTracking()
            .Where(stat => stat.UserId == userId)
            .ToListAsync(Cancel.Token);

        return (stats.Sum(stat => stat.ListenedSeconds), stats.Sum(stat => stat.PlayCount));
    }

    private async Task RecordAsync(
        Guid userId, params (Guid TrackId, DateTimeOffset At, int Plays, long Seconds)[] rows)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.ListeningStats.AddRange(rows.Select(row => new ListeningStat
        {
            UserId = userId,
            TrackId = row.TrackId,
            Hour = new DateTimeOffset(row.At.UtcDateTime.Date.AddHours(row.At.UtcDateTime.Hour), TimeSpan.Zero),
            PlayCount = row.Plays,
            ListenedSeconds = row.Seconds,
        }));

        await db.SaveChangesAsync(Cancel.Token);
    }
}
