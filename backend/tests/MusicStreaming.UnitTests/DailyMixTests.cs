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
}
