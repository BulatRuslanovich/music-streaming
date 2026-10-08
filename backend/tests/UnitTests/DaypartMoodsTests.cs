// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Xunit;
using App.Recommendations.Moods;

namespace UnitTests.Recommendations;

public class DaypartMoodsTests
{
    private const int Sad = 0;
    private const int Party = 1;

    [Theory]
    [InlineData(4, Daypart.Night)]
    [InlineData(5, Daypart.Morning)]
    [InlineData(11, Daypart.Day)]
    [InlineData(17, Daypart.Evening)]
    [InlineData(23, Daypart.Night)]
    public void The_day_splits_like_everywhere_else(int hour, Daypart expected) =>
        Assert.Equal(expected, DaypartMoods.Of(hour));

    [Fact]
    public void A_mood_heard_more_in_the_evening_gets_an_evening_lift()
    {
        // Вечером 3 из 4 — грустное, днём — 1 из 4: в целом грустного половина.
        var plays = Repeat(Daypart.Evening, Sad, 30).Concat(Repeat(Daypart.Evening, Party, 10))
            .Concat(Repeat(Daypart.Day, Sad, 10)).Concat(Repeat(Daypart.Day, Party, 30))
            .ToList();

        var lifts = DaypartMoods.Lifts(plays, 2, Daypart.Evening)!;

        Assert.Equal(0.25, lifts[Sad], 3);
        Assert.Equal(-0.25, lifts[Party], 3);
    }

    [Fact]
    public void A_listener_whose_evening_sounds_like_the_day_gets_no_lift()
    {
        var plays = Repeat(Daypart.Evening, Sad, 20).Concat(Repeat(Daypart.Evening, Party, 20))
            .Concat(Repeat(Daypart.Day, Sad, 20)).Concat(Repeat(Daypart.Day, Party, 20))
            .ToList();

        Assert.All(DaypartMoods.Lifts(plays, 2, Daypart.Evening)!, lift => Assert.Equal(0, lift, 6));
    }

    [Fact]
    public void Too_few_plays_at_this_time_mean_no_context()
    {
        var plays = Repeat(Daypart.Night, Sad, DaypartMoods.MinimumDaypartPlays - 1)
            .Concat(Repeat(Daypart.Day, Party, 100))
            .ToList();

        Assert.Null(DaypartMoods.Lifts(plays, 2, Daypart.Night));
    }

    private static IEnumerable<(Daypart, int, double)> Repeat(Daypart daypart, int mood, int count) =>
        Enumerable.Repeat((daypart, mood, 180.0), count);
}
