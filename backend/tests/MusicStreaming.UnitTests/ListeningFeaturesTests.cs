// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;
using Xunit;

namespace MusicStreaming.UnitTests;

public class ListeningFeaturesTests
{
    [Fact]
    public void Recap_uses_calendar_boundaries_in_the_listeners_time_zone()
    {
        var range = RecapMonth.Open("Europe/Moscow", new DateTimeOffset(2026, 9, 3, 10, 0, 0, TimeSpan.Zero));
        Assert.NotNull(range);
        Assert.Equal("2026-08", range.Month);
        Assert.Equal(new DateTimeOffset(2026, 7, 31, 21, 0, 0, TimeSpan.Zero), range.From);
        Assert.Equal(new DateTimeOffset(2026, 8, 31, 21, 0, 0, TimeSpan.Zero), range.Until);
    }

    [Fact]
    public void Recap_opens_only_for_the_first_week_of_the_month()
    {
        static RecapMonth? OnDay(int day) =>
            RecapMonth.Open("UTC", new DateTimeOffset(2026, 9, day, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal("2026-08", OnDay(1)?.Month);
        Assert.Equal("2026-08", OnDay(RecapMonth.WindowDays)?.Month);
        Assert.Null(OnDay(RecapMonth.WindowDays + 1));
        Assert.Null(OnDay(30));
    }

    [Fact]
    public void Recap_reads_the_window_in_local_time_and_survives_an_unknown_zone()
    {
        // 1 сентября в Окленде наступает, пока в UTC ещё 31 августа: окно открыто по местным суткам.
        var evening = new DateTimeOffset(2026, 8, 31, 20, 0, 0, TimeSpan.Zero);
        Assert.Equal("2026-08", RecapMonth.Open("Pacific/Auckland", evening)?.Month);
        Assert.Null(RecapMonth.Open("UTC", evening));

        // Postgres знает пояс, а хост .NET может и не знать — это не повод падать пятисоткой.
        Assert.Equal("2026-08", RecapMonth.Open("Mars/Olympus", new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero))?.Month);
    }

    [Fact]
    public void Recap_compares_with_the_preceding_month_across_a_year_boundary()
    {
        var january = RecapMonth.Open("America/New_York", new DateTimeOffset(2026, 2, 2, 12, 0, 0, TimeSpan.Zero));
        Assert.NotNull(january);
        Assert.Equal("2026-01", january.Month);
        Assert.Equal("2025-12", RecapMonth.Before(january, "America/New_York").Month);

        // Март в Нью-Йорке короче на час: границы переводятся в UTC по отдельности.
        var april = RecapMonth.Open("America/New_York", new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero));
        Assert.NotNull(april);
        Assert.Equal("2026-03", april.Month);
        Assert.Equal(31 * 24 - 1, (april.Until - april.From).TotalHours);
    }

    [Fact]
    public void Normalization_targets_loudness_but_limits_boost_by_true_peak()
    {
        Assert.Equal(Math.Pow(10, -6d / 20), NormalizationGain.Calculate([(new(-10, -1), 180)]), 8);
        Assert.Equal(Math.Pow(10, 1d / 20), NormalizationGain.Calculate([(new(-24, -3), 180)]), 8);
        Assert.Equal(1, NormalizationGain.Calculate([]));
        Assert.Equal(1, NormalizationGain.Calculate([(new(double.NegativeInfinity, -90), 180)]));
    }

    [Fact]
    public void An_album_has_one_gain_limited_by_its_highest_peak()
    {
        var gain = NormalizationGain.Calculate([(new(-22, -6), 180), (new(-24, -1), 360)]);
        Assert.Equal(Math.Pow(10, -1d / 20), gain, 8);
    }

    [Fact]
    public void Connect_isolates_devices_and_commands_by_account()
    {
        var registry = new ConnectRegistry(new TestClock());
        var alice = Guid.CreateVersion7();
        var bob = Guid.CreateVersion7();
        registry.Poll(alice, "laptop", Heartbeat());
        var bobs = registry.Poll(bob, "phone", Heartbeat());
        Assert.Single(bobs.Devices);
        Assert.Throws<NotFoundException>(() => registry.Send(bob, "laptop", new("pause")));
    }

    [Fact]
    public void Connect_retries_unacknowledged_commands_and_expires_stale_devices()
    {
        var clock = new TestClock();
        var registry = new ConnectRegistry(clock);
        var user = Guid.CreateVersion7();
        registry.Poll(user, "phone", Heartbeat());
        registry.Send(user, "phone", new("pause"));
        var command = Assert.Single(registry.Poll(user, "phone", Heartbeat()).Commands);
        Assert.Equal(command.Id, Assert.Single(registry.Poll(user, "phone", Heartbeat()).Commands).Id);
        Assert.Empty(registry.Poll(user, "phone", Heartbeat() with { Acknowledged = [command.Id] }).Commands);
        registry.Send(user, "phone", new("next"));
        clock.Advance(TimeSpan.FromSeconds(11));
        Assert.Empty(registry.Poll(user, "phone", Heartbeat()).Commands);
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Throws<NotFoundException>(() => registry.Send(user, "phone", new("play")));
    }

    [Fact]
    public void Transfer_preserves_queue_order_and_accounts_for_elapsed_playback()
    {
        var clock = new TestClock();
        var registry = new ConnectRegistry(clock);
        var user = Guid.CreateVersion7();
        var snapshot = new ConnectState([Guid.CreateVersion7(), Guid.CreateVersion7()], [1, 0], 1,
            30, true, 0.5, false, true, "all", "Song");
        registry.Poll(user, "laptop", Heartbeat() with { State = snapshot });
        registry.Poll(user, "phone", Heartbeat());
        clock.Advance(TimeSpan.FromSeconds(2));
        registry.Send(user, "phone", new("transfer", SourceDeviceId: "laptop"));
        var command = Assert.Single(registry.Poll(user, "phone", Heartbeat()).Commands);
        Assert.Equal(snapshot.Queue, command.State!.Queue);
        Assert.Equal(snapshot.Order, command.State.Order);
        Assert.Equal(32, command.State.Position);
        Assert.True(command.State.Shuffle);
        Assert.Equal("all", command.State.Repeat);
        Assert.Throws<ValidationException>(() => registry.Send(user, "phone", new("volume", 10)));
        Assert.Throws<ValidationException>(() => registry.Poll(user, "phone", Heartbeat() with
        {
            State = snapshot with { Order = [1, 1] },
        }));
    }

    private static ConnectHeartbeat Heartbeat() => new("Device",
        new([], [], -1, 0, false, 1, false, false, "off", null), []);
}
