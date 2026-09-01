// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services.Recommendations;
using MusicStreaming.Domain.Entities;
using MusicStreaming.Domain.Entities.Recommendations;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class StatisticsTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task An_empty_history_answers_with_zeroes_rather_than_an_error()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync();
        var stats = await GetAsync(client, StatisticsPeriod.Month);

        Assert.Equal(0, stats.Summary.ListenedSeconds);
        Assert.Equal(0, stats.Summary.Plays);
        Assert.Empty(stats.TopTracks);
        Assert.Empty(stats.ByDay);
        Assert.Null(stats.Summary.PeakDay);
    }

    [Fact]
    public async Task Listening_is_summed_per_track_artist_album_and_genre()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        var hour = DateTimeOffset.UtcNow.AddDays(-1);

        await RecordAsync(library.UserId,
            (library.Track(0), hour, 2, 400),
            (library.Track(0), hour.AddHours(1), 1, 100),
            (library.Track(1), hour, 1, 50));

        var stats = await GetAsync(client, StatisticsPeriod.Month);

        Assert.Equal(550, stats.Summary.ListenedSeconds);
        Assert.Equal(4, stats.Summary.Plays);
        Assert.Equal(2, stats.Summary.UniqueTracks);

        Assert.Equal(library.Track(0), stats.TopTracks[0].Track.Id);
        Assert.Equal(500, stats.TopTracks[0].ListenedSeconds);
        Assert.Equal(3, stats.TopTracks[0].Plays);

        Assert.NotEmpty(stats.TopAlbums);
        Assert.NotEmpty(stats.TopGenres);

        Assert.Equal(550, stats.TopArtists.Single(a => a.Id == library.Artist(0)).ListenedSeconds);
        Assert.Equal(500, stats.TopArtists.Single(a => a.Id == library.Artist(1)).ListenedSeconds);
    }

    [Fact]
    public async Task Each_period_only_counts_what_falls_inside_it()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        var now = DateTimeOffset.UtcNow;

        await RecordAsync(library.UserId,
            (library.Track(0), now.AddHours(-2), 1, 100),
            (library.Track(1), now.AddDays(-20), 1, 200),
            (library.Track(2), now.AddDays(-60), 1, 400),
            (library.Track(3), now.AddDays(-400), 1, 800));

        Assert.Equal(100, (await GetAsync(client, StatisticsPeriod.Week)).Summary.ListenedSeconds);
        Assert.Equal(300, (await GetAsync(client, StatisticsPeriod.Month)).Summary.ListenedSeconds);
        Assert.Equal(700, (await GetAsync(client, StatisticsPeriod.Quarter)).Summary.ListenedSeconds);
        Assert.Equal(1500, (await GetAsync(client, StatisticsPeriod.All)).Summary.ListenedSeconds);
    }

    [Fact]
    public async Task All_time_has_no_lower_bound_and_every_other_period_does()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync();

        Assert.Null((await GetAsync(client, StatisticsPeriod.All)).From);
        Assert.NotNull((await GetAsync(client, StatisticsPeriod.Year)).From);
    }

    [Fact]
    public async Task Days_and_hours_are_counted_in_the_listeners_own_time_zone()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        var at = new DateTimeOffset(2026, 5, 10, 22, 30, 0, TimeSpan.Zero);
        await RecordAsync(library.UserId, (library.Track(0), at, 1, 300));

        await SetTimeZoneAsync(client, "UTC");
        var utc = await GetAsync(client, StatisticsPeriod.All);

        await SetTimeZoneAsync(client, "Europe/Moscow");
        var moscow = await GetAsync(client, StatisticsPeriod.All);

        await SetTimeZoneAsync(client, "Pacific/Honolulu");
        var honolulu = await GetAsync(client, StatisticsPeriod.All);

        Assert.Equal(new DateOnly(2026, 5, 10), utc.ByDay[0].Date);
        Assert.Equal(new DateOnly(2026, 5, 11), moscow.ByDay[0].Date);
        Assert.Equal(new DateOnly(2026, 5, 10), honolulu.ByDay[0].Date);

        Assert.Equal(22, utc.ByHour[0].Hour);
        Assert.Equal(1, moscow.ByHour[0].Hour);
        Assert.Equal(12, honolulu.ByHour[0].Hour);

        Assert.Equal(300, honolulu.Summary.ListenedSeconds);

        await SetTimeZoneAsync(client, "UTC");
    }

    [Fact]
    public async Task The_busiest_day_and_hour_come_from_the_same_buckets_as_the_chart()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await SetTimeZoneAsync(client, "UTC");

        var quiet = new DateTimeOffset(2026, 5, 10, 8, 0, 0, TimeSpan.Zero);
        var busy = new DateTimeOffset(2026, 5, 12, 21, 0, 0, TimeSpan.Zero);

        await RecordAsync(library.UserId,
            (library.Track(0), quiet, 1, 100),
            (library.Track(1), busy, 3, 900));

        var stats = await GetAsync(client, StatisticsPeriod.All);

        Assert.Equal(new DateOnly(2026, 5, 12), stats.Summary.PeakDay!.Date);
        Assert.Equal(21, stats.ByHour.MaxBy(hour => hour.ListenedSeconds)!.Hour);
        Assert.Equal(2, stats.Summary.ActiveDays);
    }

    [Fact]
    public async Task The_rollup_fills_statistics_from_the_same_events_as_the_taste_profile()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        var now = DateTimeOffset.UtcNow;

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            db.PlaybackEvents.AddRange(
                Event(library, PlaybackEventType.TrackStarted, now.AddSeconds(-180), 0, 0),
                Event(library, PlaybackEventType.TrackPlayed, now.AddSeconds(-120), 60, 60),
                Event(library, PlaybackEventType.TrackPlayed, now.AddSeconds(-60), 120, 120),
                Event(library, PlaybackEventType.TrackCompleted, now, 180, 180));

            await db.SaveChangesAsync(Cancel.Token);

            await scope.ServiceProvider.GetRequiredService<ProfileRollupService>()
                .RollupAsync(library.UserId, Cancel.Token);
        }

        var stats = await GetAsync(client, StatisticsPeriod.Week);

        Assert.Equal(180, stats.Summary.ListenedSeconds);
        Assert.Equal(1, stats.Summary.Plays);

        using var check = fixture.CreateScope();
        var context = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var affinity = context.UserTrackAffinities.Single(
            a => a.UserId == library.UserId && a.TrackId == library.Track(0));

        Assert.Equal(affinity.TotalListenedSeconds, stats.Summary.ListenedSeconds);
    }

    [Fact]
    public async Task Plays_shorter_than_thirty_seconds_are_not_included_in_statistics()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        var now = DateTimeOffset.UtcNow;

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            db.PlaybackEvents.AddRange(
                Event(library, PlaybackEventType.TrackSkipped, now.AddMinutes(-2), 0, 0),
                Event(library, PlaybackEventType.TrackSkipped, now.AddMinutes(-1), 29, 29),
                Event(library, PlaybackEventType.TrackSkipped, now, 30, 30));

            await db.SaveChangesAsync(Cancel.Token);

            await scope.ServiceProvider.GetRequiredService<ProfileRollupService>()
                .RollupAsync(library.UserId, Cancel.Token);
        }

        var stats = await GetAsync(client, StatisticsPeriod.Week);

        Assert.Equal(30, stats.Summary.ListenedSeconds);
        Assert.Equal(1, stats.Summary.Plays);
    }

    [Fact]
    public async Task Running_the_rollup_twice_does_not_double_the_numbers()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            db.PlaybackEvents.Add(Event(
                library, PlaybackEventType.TrackCompleted, DateTimeOffset.UtcNow, 180, 180));

            await db.SaveChangesAsync(Cancel.Token);

            var rollup = scope.ServiceProvider.GetRequiredService<ProfileRollupService>();
            await rollup.RollupAsync(library.UserId, Cancel.Token);
            await rollup.RollupAsync(library.UserId, Cancel.Token);
        }

        Assert.Equal(180, (await GetAsync(client, StatisticsPeriod.Week)).Summary.ListenedSeconds);
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

    private static async Task<StatisticsDto> GetAsync(HttpClient client, StatisticsPeriod period)
    {
        var response = await client.GetAsync($"/api/me/statistics?period={period}", Cancel.Token);
        var body = await response.Content.ReadAsStringAsync(Cancel.Token);

        Assert.True(response.IsSuccessStatusCode, body);
        return JsonSerializer.Deserialize<StatisticsDto>(body, RecommendationApiFixture.Json)!;
    }

    private static async Task SetTimeZoneAsync(HttpClient client, string timeZone)
    {
        var response = await client.PutAsJsonAsync(
            "/api/me/settings", new UpdateUserSettingsRequest(null, null, null, timeZone));

        response.EnsureSuccessStatusCode();
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

        await db.SaveChangesAsync();
    }

}
