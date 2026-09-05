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
        var range = RecapMonth.Resolve("2026-08", "Europe/Moscow", new TestClock().GetUtcNow());
        Assert.Equal(new DateTimeOffset(2026, 7, 31, 21, 0, 0, TimeSpan.Zero), range.From);
        Assert.Equal(new DateTimeOffset(2026, 8, 31, 21, 0, 0, TimeSpan.Zero), range.Until);
    }

    [Fact]
    public void Recap_accounts_for_daylight_saving_and_defaults_to_the_previous_month()
    {
        var now = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        var range = RecapMonth.Resolve(null, "America/New_York", now);
        Assert.Equal("2026-03", range.Month);
        Assert.Equal(31 * 24 - 1, (range.Until - range.From).TotalHours);
        Assert.Throws<ValidationException>(() => RecapMonth.Resolve("2026-05", "UTC", now));
        Assert.Throws<ValidationException>(() => RecapMonth.Resolve("2026-13", "UTC", now));
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
