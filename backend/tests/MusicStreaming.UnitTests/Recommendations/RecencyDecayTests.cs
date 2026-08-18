using MusicStreaming.Application.Recommendations.Scoring;
using Xunit;

namespace MusicStreaming.UnitTests.Recommendations;

public class RecencyDecayTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);

    private const double HalfLife = 45;

    [Fact]
    public void Weight_halves_after_one_half_life() =>
        Assert.Equal(0.5, RecencyDecay.Factor(TimeSpan.FromDays(HalfLife), HalfLife), precision: 10);

    [Fact]
    public void Weight_quarters_after_two_half_lives() =>
        Assert.Equal(0.25, RecencyDecay.Factor(TimeSpan.FromDays(2 * HalfLife), HalfLife), precision: 10);

    [Fact]
    public void Nothing_decays_at_zero_age() =>
        Assert.Equal(1.0, RecencyDecay.Factor(TimeSpan.Zero, HalfLife));

    [Fact]
    public void An_event_from_the_future_is_not_amplified() =>
        Assert.Equal(1.0, RecencyDecay.Factor(TimeSpan.FromDays(-30), HalfLife));

    [Fact]
    public void Decay_is_monotonic()
    {
        var previous = double.PositiveInfinity;

        for (var days = 0; days < 400; days += 7)
        {
            var factor = RecencyDecay.Factor(TimeSpan.FromDays(days), HalfLife);
            Assert.True(factor <= previous);
            previous = factor;
        }
    }

    [Fact]
    public void A_non_positive_half_life_is_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => RecencyDecay.Factor(TimeSpan.FromDays(1), 0));

    [Fact]
    public void Accumulating_at_the_anchor_simply_adds()
    {
        var (weight, anchor) = RecencyDecay.Accumulate(2.0, Now, 1.0, Now, HalfLife);

        Assert.Equal(3.0, weight, precision: 10);
        Assert.Equal(Now, anchor);
    }

    [Fact]
    public void Accumulating_later_decays_what_was_there_and_moves_the_anchor()
    {
        var later = Now.AddDays(HalfLife);
        var (weight, anchor) = RecencyDecay.Accumulate(2.0, Now, 1.0, later, HalfLife);

        Assert.Equal(2.0, weight, precision: 10);
        Assert.Equal(later, anchor);
    }

    [Fact]
    public void An_out_of_order_event_does_not_move_the_anchor_backwards()
    {
        var older = Now.AddDays(-HalfLife);
        var (weight, anchor) = RecencyDecay.Accumulate(2.0, Now, 1.0, older, HalfLife);

        Assert.Equal(2.5, weight, precision: 10);
        Assert.Equal(Now, anchor);
    }

    [Fact]
    public void Accumulation_is_independent_of_processing_order()
    {
        var first = (Time: Now.AddDays(-60), Weight: 1.5);
        var second = (Time: Now.AddDays(-20), Weight: 2.5);
        var third = (Time: Now.AddDays(-5), Weight: -1.0);

        double Fold(params (DateTimeOffset Time, double Weight)[] events)
        {
            var weight = 0.0;
            var anchor = events[0].Time;

            foreach (var e in events)
                (weight, anchor) = RecencyDecay.Accumulate(weight, anchor, e.Weight, e.Time, HalfLife);

            return RecencyDecay.ValueAt(weight, anchor, Now, HalfLife);
        }

        var chronological = Fold(first, second, third);
        var shuffled = Fold(third, first, second);

        Assert.Equal(chronological, shuffled, precision: 10);
    }

    [Fact]
    public void Value_at_the_anchor_is_the_stored_weight() =>
        Assert.Equal(3.0, RecencyDecay.ValueAt(3.0, Now, Now, HalfLife));

    [Fact]
    public void Old_taste_fades_below_fresh_taste()
    {
        var old = RecencyDecay.ValueAt(4.0, Now.AddDays(-180), Now, HalfLife);
        var fresh = RecencyDecay.ValueAt(1.0, Now.AddDays(-1), Now, HalfLife);

        Assert.True(fresh > old);
    }
}
