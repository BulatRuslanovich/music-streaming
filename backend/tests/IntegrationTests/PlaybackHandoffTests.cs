// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Dtos;
using Xunit;

namespace IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class PlaybackHandoffTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task Another_device_sees_what_was_left_paused_and_where()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await SeedAndSignInAloneAsync();
        var (phone, laptop) = Devices();

        await ReportAsync(client, phone, "Pixel 8", [library.Track(0), library.Track(1)], index: 1, position: 74, playing: false);

        var now = await NowAsync(client, laptop);

        Assert.NotNull(now);
        Assert.Equal(phone, now.DeviceId);
        Assert.Equal("Pixel 8", now.DeviceName);
        Assert.Equal(library.Track(1), now.Track.Id);
        Assert.Equal(74, now.PositionSeconds);
        Assert.False(now.IsPlaying);
    }

    [Fact]
    public async Task The_position_of_a_device_that_is_still_playing_moves_with_the_clock()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await SeedAndSignInAloneAsync();
        var (phone, laptop) = Devices();
        var start = fixture.Clock.GetUtcNow();

        await using var session = await HoldAsync(client, phone, "Pixel 8");

        using (fixture.Clock.PinnedAt(start))
            await ReportAsync(client, phone, "Pixel 8", [library.Track(0)], index: 0, position: 30, playing: true);

        using (fixture.Clock.PinnedAt(start.AddSeconds(12)))
        {
            var now = await NowAsync(client, laptop);

            Assert.NotNull(now);
            Assert.True(now.IsPlaying);
            Assert.Equal(42, now.PositionSeconds);
        }
    }

    [Fact]
    public async Task A_device_that_dropped_its_session_is_shown_paused_where_it_was_last_heard()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await SeedAndSignInAloneAsync();
        var (phone, laptop) = Devices();
        var start = fixture.Clock.GetUtcNow();

        using (fixture.Clock.PinnedAt(start))
            await ReportAsync(client, phone, "Pixel 8", [library.Track(0)], index: 0, position: 30, playing: true);

        using (fixture.Clock.PinnedAt(start.AddMinutes(5)))
        {
            var now = await NowAsync(client, laptop);

            Assert.NotNull(now);
            Assert.False(now.IsPlaying);
            Assert.Equal(30, now.PositionSeconds);
        }
    }

    [Fact]
    public async Task The_displaced_device_learns_which_device_took_over()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await SeedAndSignInAloneAsync();
        var (phone, laptop) = Devices();

        await using var onPhone = await HoldAsync(client, phone, "Pixel 8");
        await using var onLaptop = await HoldAsync(client, laptop, "Firefox · Linux");

        var takeover = JsonSerializer.Deserialize<PlaybackTakeoverDto>(
            await onPhone.DisplacedByAsync(), RecommendationApiFixture.Json);

        Assert.Equal(new PlaybackTakeoverDto(laptop, "Firefox · Linux"), takeover);
    }

    [Fact]
    public async Task A_late_report_from_the_displaced_device_does_not_overwrite_the_new_one()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await SeedAndSignInAloneAsync();
        var (phone, laptop) = Devices();

        await using var onPhone = await HoldAsync(client, phone, "Pixel 8");
        await ReportAsync(client, phone, "Pixel 8", [library.Track(0)], index: 0, position: 10, playing: true);

        await using var onLaptop = await HoldAsync(client, laptop, "Firefox · Linux");
        await ReportAsync(client, laptop, "Firefox · Linux", [library.Track(2)], index: 0, position: 3, playing: true);

        var late = await ReportAsync(client, phone, "Pixel 8", [library.Track(0)], index: 0, position: 15, playing: false);

        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal(library.Track(2), (await NowAsync(client, phone))?.Track.Id);
    }

    [Fact]
    public async Task A_device_that_only_opened_does_not_hide_what_was_left_paused_elsewhere()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await SeedAndSignInAloneAsync();
        var (phone, laptop) = Devices();

        await ReportAsync(client, phone, "Pixel 8", [library.Track(0)], index: 0, position: 10, playing: false);

        var idle = await ReportAsync(client, laptop, "Firefox · Linux", [library.Track(3)], index: 0, position: 0, playing: false);

        Assert.Equal(HttpStatusCode.Conflict, idle.StatusCode);
        Assert.Equal(phone, (await NowAsync(client, laptop))?.DeviceId);
    }

    [Fact]
    public async Task Taking_over_continues_the_same_queue_at_the_same_moment()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await SeedAndSignInAloneAsync();
        var (phone, laptop) = Devices();
        Guid[] queue = [library.Track(0), library.Track(1), library.Track(2), library.Track(3)];

        await ReportAsync(client, phone, "Pixel 8", queue, index: 2, position: 50, playing: false, shuffle: true, repeat: "all");

        var handoff = await HandoffAsync(client, laptop);

        Assert.Equal("Pixel 8", handoff.DeviceName);
        Assert.Equal(queue, handoff.Tracks.Select(track => track.Id));
        Assert.Equal(2, handoff.Index);
        Assert.Equal(50, handoff.PositionSeconds);
        Assert.True(handoff.Shuffle);
        Assert.Equal("all", handoff.Repeat);
    }

    [Fact]
    public async Task Tracks_deleted_since_are_skipped_and_the_current_one_stays_current()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await SeedAndSignInAloneAsync();
        var (phone, laptop) = Devices();

        await ReportAsync(
            client, phone, "Pixel 8",
            [library.Track(0), library.Track(1), library.Track(2)], index: 2, position: 50, playing: false);

        var owner = await fixture.CreateSignedInClientAsync();
        (await owner.PostAsJsonAsync("/api/tracks/bulk-delete", new { ids = new[] { library.Track(1) } }, Cancel.Token))
            .EnsureSuccessStatusCode();

        var handoff = await HandoffAsync(client, laptop);

        Assert.Equal([library.Track(0), library.Track(2)], handoff.Tracks.Select(track => track.Id));
        Assert.Equal(1, handoff.Index);
        Assert.Equal(50, handoff.PositionSeconds);
    }

    [Fact]
    public async Task Playback_abandoned_overnight_is_no_longer_offered()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await SeedAndSignInAloneAsync();
        var (phone, laptop) = Devices();
        var start = fixture.Clock.GetUtcNow();

        using (fixture.Clock.PinnedAt(start))
            await ReportAsync(client, phone, "Pixel 8", [library.Track(0)], index: 0, position: 5, playing: false);

        using (fixture.Clock.PinnedAt(start.AddHours(11)))
            Assert.NotNull(await NowAsync(client, laptop));

        using (fixture.Clock.PinnedAt(start.AddHours(13)))
            Assert.Null(await NowAsync(client, laptop));
    }

    [Fact]
    public async Task There_is_nothing_to_take_over_from_yourself()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await SeedAndSignInAloneAsync();
        var (phone, _) = Devices();

        await ReportAsync(client, phone, "Pixel 8", [library.Track(0)], index: 0, position: 5, playing: false);

        Assert.Null(await NowAsync(client, phone));

        var handoff = await client.PostAsJsonAsync("/api/playback/handoff", new { deviceId = phone }, Cancel.Token);
        Assert.Equal(HttpStatusCode.NotFound, handoff.StatusCode);
    }

    private static async Task<PlaybackHandoffDto> HandoffAsync(HttpClient client, string deviceId)
    {
        var response = await client.PostAsJsonAsync("/api/playback/handoff", new { deviceId }, Cancel.Token);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PlaybackHandoffDto>(RecommendationApiFixture.Json, Cancel.Token))!;
    }

    private async Task<(SeededLibrary, HttpClient)> SeedAndSignInAloneAsync()
    {
        var (library, _) = await fixture.SeedAndSignInAsync();
        var listener = $"listener-{Guid.NewGuid():N}";

        return (library, await fixture.CreateSignedInClientAsync(listener, "listener-password"));
    }

    private static (string, string) Devices() => ($"phone-{Guid.NewGuid()}", $"laptop-{Guid.NewGuid()}");

    private static async Task<HttpResponseMessage> ReportAsync(
        HttpClient client, string deviceId, string deviceName, IReadOnlyList<Guid> trackIds,
        int index, double position, bool playing, bool shuffle = false, string repeat = "off")
    {
        var response = await client.PutAsJsonAsync(
            "/api/playback/state",
            new PlaybackStateReport(deviceId, deviceName, trackIds, index, position, playing, shuffle, repeat),
            Cancel.Token);

        return response;
    }

    private static async Task<PlaybackSession> HoldAsync(HttpClient client, string deviceId, string deviceName)
    {
        var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancel.Token);
        var response = await client.GetAsync(
            $"/api/playback/session?deviceId={deviceId}&deviceName={Uri.EscapeDataString(deviceName)}",
            HttpCompletionOption.ResponseHeadersRead,
            stop.Token);

        response.EnsureSuccessStatusCode();

        var reader = new StreamReader(await response.Content.ReadAsStreamAsync(stop.Token));
        Assert.Equal("event: claimed", await reader.ReadLineAsync(stop.Token));

        return new PlaybackSession(response, reader, stop);
    }

    private sealed class PlaybackSession(HttpResponseMessage response, StreamReader reader, CancellationTokenSource stop)
        : IAsyncDisposable
    {
        public async Task<string> DisplacedByAsync()
        {
            while (await reader.ReadLineAsync(stop.Token) is { } line)
            {
                if (line != "event: displaced")
                    continue;

                var data = await reader.ReadLineAsync(stop.Token);
                return data!["data: ".Length..];
            }

            throw new InvalidOperationException("The session ended without being displaced.");
        }

        public async ValueTask DisposeAsync()
        {
            await stop.CancelAsync();
            reader.Dispose();
            response.Dispose();
            stop.Dispose();
        }
    }

    private static async Task<PlayingElsewhereDto?> NowAsync(HttpClient client, string deviceId)
    {
        var response = await client.GetAsync($"/api/playback/now?deviceId={deviceId}", Cancel.Token);
        response.EnsureSuccessStatusCode();

        return response.StatusCode == HttpStatusCode.NoContent
            ? null
            : await response.Content.ReadFromJsonAsync<PlayingElsewhereDto>(RecommendationApiFixture.Json, Cancel.Token);
    }
}
