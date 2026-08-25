// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Services;
using Xunit;

namespace MusicStreaming.UnitTests;

public class DailyMixTests
{
    private static readonly Guid Listener = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Someone = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateOnly Today = new(2026, 8, 19);
    private static readonly DateOnly Tomorrow = new(2026, 8, 20);

    private static Guid[] Pool(int count) =>
        [.. Enumerable.Range(1, count).Select(index => Guid.Parse($"00000000-0000-0000-0000-{index:D12}"))];

    [Fact]
    public void The_same_listener_gets_the_same_mix_all_day()
    {
        var pool = Pool(50);

        Assert.Equal(
            DailyMix.Pick(Listener, Today, pool, 20),
            DailyMix.Pick(Listener, Today, pool, 20));
    }

    [Fact]
    public void A_new_day_reshuffles_the_mix()
    {
        var pool = Pool(50);

        Assert.NotEqual(
            DailyMix.Pick(Listener, Today, pool, 20),
            DailyMix.Pick(Listener, Tomorrow, pool, 20));
    }

    [Fact]
    public void Two_listeners_do_not_share_a_mix()
    {
        var pool = Pool(50);

        Assert.NotEqual(
            DailyMix.Pick(Listener, Today, pool, 20),
            DailyMix.Pick(Someone, Today, pool, 20));
    }

    [Fact]
    public void The_order_the_pool_arrives_in_does_not_matter()
    {
        var pool = Pool(50);

        Assert.Equal(
            DailyMix.Pick(Listener, Today, pool, 20),
            DailyMix.Pick(Listener, Today, pool.Reverse(), 20));
    }

    [Fact]
    public void A_pool_smaller_than_the_mix_is_returned_whole()
    {
        var mix = DailyMix.Pick(Listener, Today, Pool(3), 20);

        Assert.Equal(3, mix.Count);
        Assert.Equal(Pool(3).Order(), mix.Order());
    }

    [Fact]
    public void Repeats_in_the_pool_are_dropped()
    {
        var pool = Pool(5);

        Assert.Equal(5, DailyMix.Pick(Listener, Today, [.. pool, .. pool], 20).Count);
    }

    [Fact]
    public void Asking_for_nothing_yields_nothing()
    {
        Assert.Empty(DailyMix.Pick(Listener, Today, Pool(50), 0));
        Assert.Empty(DailyMix.Pick(Listener, Today, Pool(50), -1));
    }

    [Fact]
    public void An_empty_pool_is_not_an_error() =>
        Assert.Empty(DailyMix.Pick(Listener, Today, [], 20));

    [Fact]
    public void The_weighted_mix_favours_the_stronger_scores()
    {
        var pool = Pool(60);

        // Первая половина пула вдесятеро сильнее второй.
        var weighted = pool.Select((id, index) => (id, Weight: index < 30 ? 1.0 : 0.1)).ToList();

        var mix = DailyMix.PickWeighted(Listener, Today, weighted, 20).ToHashSet();
        var strong = mix.Count(id => Array.IndexOf(pool, id) < 30);

        Assert.True(strong > 14, $"ожидалось преобладание сильных треков, получено {strong} из 20");
    }

    [Fact]
    public void The_weighted_mix_is_stable_all_day_and_reshuffles_tomorrow()
    {
        var weighted = Pool(50).Select((id, index) => (id, Weight: 1.0 - index * 0.01)).ToList();

        Assert.Equal(
            DailyMix.PickWeighted(Listener, Today, weighted, 20),
            DailyMix.PickWeighted(Listener, Today, weighted, 20));

        Assert.NotEqual(
            DailyMix.PickWeighted(Listener, Today, weighted, 20),
            DailyMix.PickWeighted(Listener, Tomorrow, weighted, 20));
    }

    [Fact]
    public void A_zero_scored_track_can_still_reach_the_weighted_mix()
    {
        var pool = Pool(30);
        var weighted = pool.Select(id => (id, Weight: 0.0)).ToList();

        Assert.Equal(20, DailyMix.PickWeighted(Listener, Today, weighted, 20).Count);
    }

    [Fact]
    public void The_weighted_mix_drops_repeats_and_keeps_the_best_score()
    {
        var pool = Pool(5);
        var weighted = pool.Concat(pool).Select((id, index) => (id, Weight: index * 0.1)).ToList();

        Assert.Equal(5, DailyMix.PickWeighted(Listener, Today, weighted, 20).Count);
    }
}
