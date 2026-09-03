// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities;
using MusicStreaming.Domain.Entities.Recommendations;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class AdminStatisticsTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task An_ordinary_listener_cannot_read_the_statistics_of_the_service()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        await fixture.SeedAndSignInAsync();
        var listener = await fixture.CreateSignedInClientAsync("statsoutsider", "outsider-password");

        foreach (var path in new[]
                 {
                     "/api/admin/statistics/overview",
                     "/api/admin/statistics/catalog",
                     "/api/admin/statistics/users",
                     $"/api/admin/statistics/users/{Guid.CreateVersion7()}",
                     "/api/admin/statistics/uploads",
                 })
        {
            var response = await listener.GetAsync(path, Cancel.Token);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task The_overview_counts_the_library_the_people_and_what_they_listened_to()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await RecordAsync(library.UserId, (library.Track(0), Hour(-2), 3, 600));

        var overview = await GetAsync<AdminOverviewDto>(client, "/api/admin/statistics/overview");

        Assert.Equal(20, overview.Library.TotalTracks);
        Assert.True(overview.Users.Total >= 1);
        Assert.Equal(1, overview.Users.Active);
        Assert.Equal(600, overview.Listening.ListenedSeconds);
        Assert.Equal(3, overview.Listening.Plays);
        Assert.Equal(1, overview.Listening.UniqueListeners);
        Assert.Equal(1, overview.Listening.UniqueTracks);

        // Источники перечислены целиком, даже когда по ним ничего нет — график не должен
        // менять форму от того, что за период никто ничего не импортировал.
        Assert.Equal(
            Enum.GetValues<IngestionSource>().Length, overview.UploadsBySource.Count);
    }

    [Fact]
    public async Task A_skip_rate_without_any_events_is_zero_rather_than_a_division_by_zero()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync();

        var overview = await GetAsync<AdminOverviewDto>(client, "/api/admin/statistics/overview");

        Assert.Equal(0, overview.Listening.Completed);
        Assert.Equal(0, overview.Listening.Skipped);
        Assert.Equal(0, overview.Listening.SkipRate);
    }

    [Fact]
    public async Task The_skip_rate_is_the_share_of_skips_among_the_plays_that_ended()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        await RecordEventsAsync(
            library.UserId,
            library.Track(0),
            (PlaybackEventType.TrackCompleted, 1),
            (PlaybackEventType.TrackSkipped, 3));

        var overview = await GetAsync<AdminOverviewDto>(client, "/api/admin/statistics/overview");

        Assert.Equal(1, overview.Listening.Completed);
        Assert.Equal(3, overview.Listening.Skipped);
        Assert.Equal(0.75, overview.Listening.SkipRate, 3);
    }

    [Fact]
    public async Task A_period_leaves_out_what_happened_before_it()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        await RecordAsync(
            library.UserId,
            (library.Track(0), Hour(-2), 1, 100),
            (library.Track(1), Hour(-24 * 45), 1, 900));

        var month = await GetAsync<AdminOverviewDto>(
            client, "/api/admin/statistics/overview?period=Month");
        var all = await GetAsync<AdminOverviewDto>(
            client, "/api/admin/statistics/overview?period=All");

        Assert.Equal(100, month.Listening.ListenedSeconds);
        Assert.Equal(1000, all.Listening.ListenedSeconds);
    }

    [Fact]
    public async Task One_listener_never_picks_up_the_statistics_of_another()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        var other = await OtherUserAsync("statsneighbour", "neighbour-password");

        await RecordAsync(library.UserId, (library.Track(0), Hour(-1), 2, 400));
        await RecordAsync(other, (library.Track(1), Hour(-1), 5, 999));

        var owner = await ListenerAsync(client, library.UserId);
        var neighbour = await ListenerAsync(client, other);

        Assert.Equal(400, owner.ListenedSeconds);
        Assert.Equal(2, owner.Plays);
        Assert.Equal(999, neighbour.ListenedSeconds);
        Assert.Equal(5, neighbour.Plays);
    }

    [Fact]
    public async Task A_listener_who_never_played_anything_gets_zeroes_and_not_a_missing_page()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync();
        var quiet = await OtherUserAsync("statssilent", "silent-password");

        var detail = await GetAsync<AdminListenerDetailDto>(
            client, $"/api/admin/statistics/users/{quiet}");

        Assert.Equal(quiet, detail.Listener.Id);
        Assert.Equal(0, detail.Listener.ListenedSeconds);
        Assert.Equal(0, detail.Listener.Plays);
        Assert.Equal(0, detail.Listener.SkipRate);
        Assert.Empty(detail.TopTracks);
        Assert.Empty(detail.TopArtists);
        Assert.Empty(detail.ByDay);
        Assert.Empty(detail.RecentUploads);
    }

    [Fact]
    public async Task Asking_about_an_account_that_does_not_exist_is_a_missing_page()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync();

        var response = await client.GetAsync(
            $"/api/admin/statistics/users/{Guid.CreateVersion7()}", Cancel.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_listener_list_pages_and_sorts_on_the_server()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        var loud = await OtherUserAsync("statsloud", "loud-password");

        await RecordAsync(library.UserId, (library.Track(0), Hour(-1), 1, 10));
        await RecordAsync(loud, (library.Track(1), Hour(-1), 1, 5_000));

        var descending = await PageAsync(
            client, "/api/admin/statistics/users?sort=ListenedSeconds&direction=desc");
        var ascending = await PageAsync(
            client, "/api/admin/statistics/users?sort=ListenedSeconds&direction=asc");

        // Утверждения о самой упорядоченности, а не о конкретной позиции: соседние тесты заводят
        // собственные аккаунты, и «последний на первой странице» зависел бы от их числа.
        Assert.Equal(loud, descending.Items[0].Id);
        Assert.NotEqual(loud, ascending.Items[0].Id);

        Assert.Equal(
            descending.Items.Select(u => u.ListenedSeconds).OrderDescending(),
            descending.Items.Select(u => u.ListenedSeconds));
        Assert.Equal(
            ascending.Items.Select(u => u.ListenedSeconds).Order(),
            ascending.Items.Select(u => u.ListenedSeconds));

        var firstPage = await PageAsync(client, "/api/admin/statistics/users?page=1&pageSize=1");

        Assert.Single(firstPage.Items);
        Assert.True(firstPage.Total >= 2);
        Assert.Equal(1, firstPage.Page);
    }

    [Fact]
    public async Task The_listener_list_can_be_searched_by_name()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync();
        var found = await OtherUserAsync("statsneedle", "needle-password");

        var page = await PageAsync(client, "/api/admin/statistics/users?q=needle");

        Assert.Equal(found, Assert.Single(page.Items).Id);
    }

    [Fact]
    public async Task The_state_of_the_catalogue_names_its_gaps_and_the_threshold_it_used()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync();

        var health = await GetAsync<AdminCatalogHealthDto>(client, "/api/admin/statistics/catalog");

        Assert.Equal(20, health.TotalTracks);

        // Сид кладёт треки без текста и без обложек — иначе показатель нечем проверять.
        Assert.Equal(20, health.WithoutLyrics);
        Assert.Equal(20, health.WithoutCover);
        Assert.Equal(20, health.NeverListened);
        Assert.True(health.HighSkipRateThreshold is > 0 and < 1);
        Assert.True(health.HighSkipRateMinimumEvents > 1);
    }

    private async Task<AdminListenerDto> ListenerAsync(HttpClient client, Guid userId)
    {
        var detail = await GetAsync<AdminListenerDetailDto>(
            client, $"/api/admin/statistics/users/{userId}");

        return detail.Listener;
    }

    private async Task<Guid> OtherUserAsync(string username, string password)
    {
        await fixture.CreateSignedInClientAsync(username, password);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.Users.Where(u => u.Username == username).Select(u => u.Id).SingleAsync();
    }

    private static DateTimeOffset Hour(int offsetHours)
    {
        var at = DateTimeOffset.UtcNow.AddHours(offsetHours);

        return new DateTimeOffset(at.Year, at.Month, at.Day, at.Hour, 0, 0, TimeSpan.Zero);
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
            Hour = row.At,
            PlayCount = row.Plays,
            ListenedSeconds = row.Seconds,
        }));

        await db.SaveChangesAsync();
    }

    private async Task RecordEventsAsync(
        Guid userId, Guid trackId, params (PlaybackEventType Type, int Count)[] events)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        foreach (var (type, count) in events)
        {
            for (var i = 0; i < count; i++)
            {
                db.PlaybackEvents.Add(new PlaybackEvent
                {
                    UserId = userId,
                    TrackId = trackId,
                    Type = type,
                    OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-i),
                    SessionId = Guid.CreateVersion7(),
                    Source = PlaybackSource.Unknown,
                });
            }
        }

        await db.SaveChangesAsync();
    }

    private static async Task<PagedResult<AdminListenerDto>> PageAsync(HttpClient client, string path) =>
        await GetAsync<PagedResult<AdminListenerDto>>(client, path);

    private static async Task<T> GetAsync<T>(HttpClient client, string path)
    {
        var response = await client.GetAsync(path, Cancel.Token);
        var body = await response.Content.ReadAsStringAsync(Cancel.Token);

        Assert.True(response.IsSuccessStatusCode, body);

        return JsonSerializer.Deserialize<T>(body, RecommendationApiFixture.Json)!;
    }
}
