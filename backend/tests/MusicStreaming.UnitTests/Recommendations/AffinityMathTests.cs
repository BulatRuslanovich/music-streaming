using MusicStreaming.Application.Recommendations.Scoring;
using MusicStreaming.Domain.Entities.Recommendations;
using Xunit;

namespace MusicStreaming.UnitTests.Recommendations;

public class AffinityMathTests
{
    private const double Softness = 3.0;

    [Fact]
    public void Nothing_normalises_to_nothing() =>
        Assert.Equal(0, AffinityMath.Normalize(0, Softness));

    [Theory]
    [InlineData(1000.0)]
    [InlineData(-1000.0)]
    public void Normalisation_stays_inside_the_unit_interval(double weight)
    {
        var score = AffinityMath.Normalize(weight, Softness);

        Assert.True(Math.Abs(score) < 1);
        Assert.True(Math.Abs(score) > 0.99);
    }

    [Fact]
    public void Normalisation_is_symmetric() =>
        Assert.Equal(
            -AffinityMath.Normalize(5, Softness),
            AffinityMath.Normalize(-5, Softness),
            precision: 10);

    [Fact]
    public void Normalisation_preserves_order()
    {
        Assert.True(AffinityMath.Normalize(10, Softness) > AffinityMath.Normalize(4, Softness));
        Assert.True(AffinityMath.Normalize(4, Softness) > AffinityMath.Normalize(-1, Softness));
    }

    /// <summary>
    /// The point of squashing: one obsessively replayed track must not sit orders of magnitude
    /// above everything else and collapse every shelf onto itself.
    /// </summary>
    [Fact]
    public void An_obsession_does_not_dwarf_a_normal_preference()
    {
        var obsession = AffinityMath.Normalize(200, Softness);
        var ordinary = AffinityMath.Normalize(4, Softness);

        Assert.True(obsession > ordinary);
        Assert.True(obsession / ordinary < 2);
    }

    [Fact]
    public void A_non_positive_softness_is_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AffinityMath.Normalize(1, 0));

    [Theory]
    [InlineData(0, ProfileMaturity.Cold)]
    [InlineData(9, ProfileMaturity.Cold)]
    [InlineData(10, ProfileMaturity.Warm)]
    [InlineData(99, ProfileMaturity.Warm)]
    [InlineData(100, ProfileMaturity.Mature)]
    [InlineData(10_000, ProfileMaturity.Mature)]
    public void Maturity_follows_the_evidence(int signals, ProfileMaturity expected) =>
        Assert.Equal(expected, AffinityMath.MaturityFor(signals, 10, 100));

    [Fact]
    public void No_support_means_no_similarity() =>
        Assert.Equal(0, AffinityMath.Shrink(1.0, support: 0, lambda: 5));

    /// <summary>
    /// One shared session between two tracks is a coincidence. Without shrinkage it reads as
    /// perfect similarity, which is exactly the failure mode of sparse early data.
    /// </summary>
    [Fact]
    public void A_single_observation_is_pulled_towards_zero()
    {
        var shrunk = AffinityMath.Shrink(1.0, support: 1, lambda: 5);

        Assert.True(shrunk < 0.2);
    }

    [Fact]
    public void Shrinkage_relaxes_as_evidence_accumulates()
    {
        var thin = AffinityMath.Shrink(1.0, support: 1, lambda: 5);
        var solid = AffinityMath.Shrink(1.0, support: 50, lambda: 5);

        Assert.True(solid > thin);
        Assert.True(solid > 0.9);
        Assert.True(solid < 1.0);
    }

    [Fact]
    public void Freshness_falls_from_one_to_zero_across_the_window()
    {
        var now = new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(1, AffinityMath.Freshness(now, now, 30));
        Assert.Equal(0.5, AffinityMath.Freshness(now.AddDays(-15), now, 30), precision: 6);
        Assert.Equal(0, AffinityMath.Freshness(now.AddDays(-30), now, 30));
        Assert.Equal(0, AffinityMath.Freshness(now.AddDays(-365), now, 30));
    }

    [Fact]
    public void Something_added_in_the_future_is_simply_new()
    {
        var now = new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(1, AffinityMath.Freshness(now.AddDays(1), now, 30));
    }
}
