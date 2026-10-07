// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using App.Dtos;
using Domain.Entities.Recommendations;
using Infrastructure.Persistence;
using Xunit;

namespace IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class RecapTests(RecommendationApiFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_first_week_of_a_month_sums_up_the_previous_one()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        using var pinned = fixture.Clock.PinnedAt(Now);

        // Август: Artist 1 уже знаком. Сентябрь: Artist 0 слушают впервые. Октябрь в итоги не входит.
        await PlayAsync(library.UserId,
            (library.Track(5), new DateTimeOffset(2026, 8, 15, 10, 0, 0, TimeSpan.Zero), 120),
            (library.Track(0), new DateTimeOffset(2026, 9, 3, 9, 0, 0, TimeSpan.Zero), 180),
            (library.Track(0), new DateTimeOffset(2026, 9, 3, 21, 0, 0, TimeSpan.Zero), 180),
            (library.Track(0), new DateTimeOffset(2026, 9, 10, 21, 30, 0, TimeSpan.Zero), 180),
            (library.Track(1), new DateTimeOffset(2026, 9, 10, 22, 0, 0, TimeSpan.Zero), 200),
            (library.Track(5), new DateTimeOffset(2026, 9, 30, 23, 0, 0, TimeSpan.Zero), 100),
            (library.Track(10), new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero), 10),
            (library.Track(15), new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.Zero), 300));

        var recap = (await client.GetFromJsonAsync<RecapDto>("/api/recap", Cancel.Token))!;

        Assert.Equal((2026, 9), (recap.Year, recap.Month));
        Assert.Equal(850, recap.ListenedSeconds);
        Assert.Equal(5, recap.Plays);
        Assert.Equal(120, recap.PreviousListenedSeconds);
        Assert.Equal(library.Track(0), recap.TopTracks[0].Track.Id);
        Assert.Equal(3, recap.TopTracks[0].Plays);
        Assert.DoesNotContain(recap.TopTracks, item => item.Track.Id == library.Track(10));
        Assert.DoesNotContain(recap.TopTracks, item => item.Track.Id == library.Track(15));
        Assert.Equal(library.Artist(0), recap.TopArtists[0].Artist.Id);
        Assert.Equal(1, recap.NewArtists);
        Assert.Equal([library.Artist(0)], recap.NewArtistPicks.Select(artist => artist.Id));
        Assert.Equal(30, recap.DaySeconds.Count);
        Assert.Equal(360, recap.DaySeconds[2]);
        Assert.Equal(360, recap.HourSeconds[21]);
        Assert.Equal(200, recap.HourSeconds[22]);
        Assert.NotNull(recap.TopGenre);
    }

    [Fact]
    public async Task The_first_month_on_record_has_no_discoveries_to_claim()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        using var pinned = fixture.Clock.PinnedAt(Now);

        await PlayAsync(library.UserId, (library.Track(0), new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero), 200));

        var recap = (await client.GetFromJsonAsync<RecapDto>("/api/recap", Cancel.Token))!;

        Assert.Null(recap.NewArtists);
        Assert.Null(recap.PreviousListenedSeconds);
    }

    [Fact]
    public async Task After_the_first_week_there_is_no_recap()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        using var pinned = fixture.Clock.PinnedAt(new DateTimeOffset(2026, 10, 8, 0, 30, 0, TimeSpan.Zero));

        await PlayAsync(library.UserId, (library.Track(0), new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero), 200));

        var response = await client.GetAsync("/api/recap", Cancel.Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task A_quiet_month_has_no_recap()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        using var pinned = fixture.Clock.PinnedAt(Now);

        await PlayAsync(library.UserId, (library.Track(0), new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero), 10));

        var response = await client.GetAsync("/api/recap", Cancel.Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // В Москве уже 1 ноября, в UTC ещё 31 октября: неделя итогов и границы месяца — по часам слушателя.
    [Fact]
    public async Task The_week_and_the_month_follow_the_listeners_time_zone()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        using var pinned = fixture.Clock.PinnedAt(new DateTimeOffset(2026, 10, 31, 22, 30, 0, TimeSpan.Zero));

        // 22:30 UTC 30 сентября — уже 01:30 1 октября в Москве.
        await PlayAsync(library.UserId, (library.Track(0), new DateTimeOffset(2026, 9, 30, 22, 30, 0, TimeSpan.Zero), 200));

        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/recap", Cancel.Token)).StatusCode);

        (await client.PutAsJsonAsync("/api/me/settings", new { timeZone = "Europe/Moscow" }, Cancel.Token))
            .EnsureSuccessStatusCode();

        try
        {
            var recap = (await client.GetFromJsonAsync<RecapDto>("/api/recap", Cancel.Token))!;

            Assert.Equal((2026, 10), (recap.Year, recap.Month));
            Assert.Equal(200, recap.DaySeconds[0]);
            Assert.Equal(200, recap.HourSeconds[1]);
        }
        finally
        {
            (await client.PutAsJsonAsync("/api/me/settings", new { timeZone = "UTC" }, Cancel.Token))
                .EnsureSuccessStatusCode();
        }
    }

    private async Task PlayAsync(Guid userId, params (Guid TrackId, DateTimeOffset At, int Seconds)[] plays)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.PlaybackEvents.AddRange(plays.Select(play => new PlaybackEvent
        {
            UserId = userId,
            TrackId = play.TrackId,
            Type = PlaybackEventType.TrackCompleted,
            OccurredAt = play.At,
            ListenedSeconds = play.Seconds,
            DurationSeconds = Math.Max(play.Seconds, 180),
            SessionId = Guid.NewGuid(),
        }));

        await db.SaveChangesAsync(Cancel.Token);
    }
}
