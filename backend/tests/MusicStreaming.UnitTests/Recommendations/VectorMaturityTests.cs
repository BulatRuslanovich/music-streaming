// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Recommendations.Scoring;
using Xunit;

namespace MusicStreaming.UnitTests.Recommendations;

public class VectorMaturityTests
{
    private const int FormingAt = 3;
    private const int ReadyAt = 8;

    [Theory]
    [InlineData(0, VectorMaturityLevel.Discovering)]
    [InlineData(2, VectorMaturityLevel.Discovering)]
    [InlineData(3, VectorMaturityLevel.Forming)]
    [InlineData(7, VectorMaturityLevel.Forming)]
    [InlineData(8, VectorMaturityLevel.Ready)]
    [InlineData(500, VectorMaturityLevel.Ready)]
    public void Maturity_follows_the_positive_count(int positives, VectorMaturityLevel expected) =>
        Assert.Equal(expected, VectorMaturity.Of(positives, FormingAt, ReadyAt));

    [Fact]
    public void A_newcomer_explores_the_most_and_a_regular_the_least()
    {
        const double Base = 0.15;
        const double Discover = 0.35;

        var discovering = VectorMaturity.EffectiveExplore(Base, Discover, VectorMaturityLevel.Discovering);
        var forming = VectorMaturity.EffectiveExplore(Base, Discover, VectorMaturityLevel.Forming);
        var ready = VectorMaturity.EffectiveExplore(Base, Discover, VectorMaturityLevel.Ready);

        Assert.Equal(0.35, discovering, precision: 6);
        Assert.Equal(0.25, forming, precision: 6);
        Assert.Equal(0.15, ready, precision: 6);
    }

    [Fact]
    public void A_discover_ratio_below_the_base_does_not_make_a_newcomer_conservative()
    {
        // Настройка «discover меньше base» бессмысленна, но не должна выворачивать поведение.
        Assert.Equal(0.4, VectorMaturity.EffectiveExplore(0.4, 0.1, VectorMaturityLevel.Discovering), precision: 6);
    }

    [Fact]
    public void Negative_signals_never_demote_the_vector()
    {
        // Зрелость считается только по положительным: человек, который много слушал и много
        // пропускал, всё равно сообщил системе много.
        Assert.Equal(VectorMaturityLevel.Ready, VectorMaturity.Of(ReadyAt, FormingAt, ReadyAt));
    }
}
