// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Xunit;
using Infrastructure.Audio;

namespace UnitTests;

public class ClapWindowPlannerTests
{
    [Fact]
    public void A_track_no_longer_than_a_window_is_heard_once_from_the_start()
    {
        Assert.Empty(ClapWindowPlanner.Plan(0));
        Assert.Equal([0.0], ClapWindowPlanner.Plan(8));
        Assert.Equal([0.0], ClapWindowPlanner.Plan(10));
    }

    [Theory]
    [InlineData(45, 3)]
    [InlineData(150, 5)]
    [InlineData(240, 8)]
    [InlineData(900, 8)]
    public void Longer_tracks_get_one_window_per_half_minute_within_bounds(double duration, int expected) =>
        Assert.Equal(expected, ClapWindowPlanner.Plan(duration).Count);

    // Вступление и концовка нетипичны для трека: окна их обходят.
    [Fact]
    public void A_song_is_heard_across_its_body_not_at_the_edges()
    {
        var offsets = ClapWindowPlanner.Plan(240);

        Assert.Equal(12, offsets[0], 3);
        Assert.Equal(240 - 10 - 12, offsets[^1], 3);
        Assert.All(offsets.Zip(offsets.Skip(1)), pair => Assert.True(pair.Second > pair.First));
    }

    [Fact]
    public void Edges_never_eat_more_than_fifteen_seconds()
    {
        var offsets = ClapWindowPlanner.Plan(1200);

        Assert.Equal(15, offsets[0], 3);
        Assert.Equal(1200 - 10 - 15, offsets[^1], 3);
    }

    [Theory]
    [InlineData(10.5)]
    [InlineData(12)]
    [InlineData(31)]
    [InlineData(240)]
    [InlineData(3600)]
    public void Every_window_fits_inside_the_track(double duration) =>
        Assert.All(ClapWindowPlanner.Plan(duration), offset =>
        {
            Assert.True(offset >= 0, $"offset {offset}");
            Assert.True(offset + ClapWindowPlanner.WindowSeconds <= duration + 0.05, $"offset {offset}");
        });

    [Fact]
    public void A_barely_longer_track_does_not_repeat_the_same_window()
    {
        var offsets = ClapWindowPlanner.Plan(10.3);

        Assert.Equal([0.0], offsets);
    }
}
