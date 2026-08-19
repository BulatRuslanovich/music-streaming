// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services.Integrations;
using MusicStreaming.Domain.Entities.Integrations;
using MusicStreaming.Domain.Entities.Recommendations;
using MusicStreaming.Infrastructure.Integrations;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class ScrobbleQueueingTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task Nothing_is_queued_for_a_listener_without_a_connected_account()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var library = await SeedAsync(connect: false);
        await QueueAsync(library, Finished(library, listened: 180));

        Assert.Empty(await JobsAsync());
    }

    [Fact]
    public async Task A_finished_play_becomes_a_scrobble()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var library = await SeedAsync(connect: true);
        await QueueAsync(library, Finished(library, listened: 180));

        var job = Assert.Single(await JobsAsync());

        Assert.Equal(OutboundJobKind.LastfmScrobble, job.Kind);
        Assert.Equal(OutboundJobState.Pending, job.State);

        var payload = ScrobbleQueueing.ReadPayload(job.Payload)!;
        Assert.Equal("Track 0", payload.Title);
        Assert.Equal("Artist 0", payload.Artist);
        Assert.NotNull(payload.PlayedAtUnix);
    }

    [Fact]
    public async Task Starting_a_track_becomes_a_now_playing_update()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var library = await SeedAsync(connect: true);
        await QueueAsync(library, Event(library, PlaybackEventType.TrackStarted, 0, 0));

        var job = Assert.Single(await JobsAsync());

        Assert.Equal(OutboundJobKind.LastfmNowPlaying, job.Kind);
        Assert.Null(ScrobbleQueueing.ReadPayload(job.Payload)!.PlayedAtUnix);
    }

    [Fact]
    public async Task An_abandoned_track_is_not_scrobbled()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var library = await SeedAsync(connect: true);
        await QueueAsync(library, Event(library, PlaybackEventType.TrackSkipped, 20, 20));

        Assert.Empty(await JobsAsync());
    }

    [Fact]
    public async Task The_same_play_arriving_twice_is_queued_once()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var library = await SeedAsync(connect: true);
        var finished = Finished(library, listened: 180);

        await QueueAsync(library, finished);
        await QueueAsync(library, finished);

        Assert.Single(await JobsAsync());
    }

    [Fact]
    public async Task Playing_the_same_track_again_later_is_a_second_scrobble()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var library = await SeedAsync(connect: true);

        await QueueAsync(library, Finished(library, listened: 180));
        await QueueAsync(library, Finished(library, listened: 180, at: DateTimeOffset.UtcNow.AddHours(1)));

        Assert.Equal(2, (await JobsAsync()).Count);
    }

    [Fact]
    public async Task Disconnecting_drops_everything_still_waiting_to_be_sent()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var library = await SeedAsync(connect: true);
        await QueueAsync(library, Finished(library, listened: 180));

        Assert.NotEmpty(await JobsAsync());

        var client = await fixture.CreateSignedInClientAsync();
        (await client.DeleteAsync("/api/lastfm", Cancel.Token)).EnsureSuccessStatusCode();

        Assert.Empty(await JobsAsync());
    }

    [Fact]
    public async Task The_integration_is_not_offered_when_the_server_has_no_credentials()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var status = await client.GetFromJsonAsync<LastfmStatusDto>("/api/lastfm/status", Cancel.Token);

        Assert.False(status!.Available);
        Assert.Null(status.Username);
    }

    private static PlaybackEvent Finished(SeededLibrary library, int listened, DateTimeOffset? at = null) =>
        Event(library, PlaybackEventType.TrackCompleted, listened, listened, at);

    private static PlaybackEvent Event(
        SeededLibrary library,
        PlaybackEventType type,
        int position,
        int listened,
        DateTimeOffset? at = null) => new()
        {
            UserId = library.UserId,
            TrackId = library.Track(0),
            Type = type,
            OccurredAt = at ?? new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
            PositionSeconds = position,
            ListenedSeconds = listened,
            DurationSeconds = 180,
            SessionId = Guid.CreateVersion7(),
        };

    private async Task QueueAsync(SeededLibrary library, params PlaybackEvent[] events)
    {
        using var scope = fixture.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ScrobbleQueueing>().QueueAsync(events);
    }

    private async Task<List<OutboundJob>> JobsAsync()
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.OutboundJobs.AsNoTracking().OrderBy(job => job.CreatedAt).ToListAsync();
    }

    private async Task<SeededLibrary> SeedAsync(bool connect)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var library = await LibrarySeeder.SeedAsync(db);

        if (connect)
        {
            var secrets = scope.ServiceProvider.GetRequiredService<ISecretProtector>();

            db.LastfmAccounts.Add(new LastfmAccount
            {
                UserId = library.UserId,
                Username = "listener",
                SessionKey = secrets.Protect("session-key"),
            });

            await db.SaveChangesAsync();
        }

        return library;
    }
}

public class OutboundRetryTests
{
    [Fact]
    public void A_temporary_failure_is_retried_with_growing_delays()
    {
        var delays = Enumerable.Range(1, OutboundRetry.Backoff.Length)
            .Select(attempt => OutboundRetry.DelayFor(OutboundJobKind.LastfmScrobble, attempt, Unavailable))
            .ToList();

        Assert.All(delays, delay => Assert.NotNull(delay));
        Assert.Equal(delays.OrderBy(delay => delay), delays);
    }

    [Fact]
    public void Retries_stop_once_the_schedule_runs_out() =>
        Assert.Null(OutboundRetry.DelayFor(
            OutboundJobKind.LastfmScrobble, OutboundRetry.Backoff.Length + 1, Unavailable));

    [Fact]
    public void A_permanent_refusal_is_never_retried() =>
        Assert.Null(OutboundRetry.DelayFor(
            OutboundJobKind.LastfmScrobble, 1, new LastfmException("Invalid signature")));

    [Fact]
    public void A_dead_session_is_never_retried()
    {
        var failure = new LastfmException("Invalid session key", Transient: true, AuthFailure: true);

        Assert.Null(OutboundRetry.DelayFor(OutboundJobKind.LastfmScrobble, 1, failure));
    }

    [Fact]
    public void Now_playing_is_never_retried()
    {
        Assert.Null(OutboundRetry.DelayFor(OutboundJobKind.LastfmNowPlaying, 1, Unavailable));
    }

    private static LastfmException Unavailable => new("Service unavailable", Transient: true);
}
