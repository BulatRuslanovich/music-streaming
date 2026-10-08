// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Xunit;
using App.Recommendations;
using Domain.Entities.Recommendations;

namespace UnitTests.Recommendations;

public class SkipFactorTests
{
    [Theory]
    [InlineData("home:forYou")]
    [InlineData("home:discover")]
    [InlineData("radio")]
    [InlineData("radio:mood:sad")]
    [InlineData("radio:autoplay")]
    [InlineData("mix:daily")]
    public void A_dropped_recommendation_counts_more(string source) =>
        Assert.Equal(EventWeights.RecommendationSkipFactor, EventWeights.SkipFactor(source));

    [Theory]
    [InlineData("album")]
    [InlineData("playlist")]
    [InlineData("favorites")]
    [InlineData("history")]
    [InlineData("mix:new")]
    public void A_skip_while_browsing_ones_own_list_counts_less(string source) =>
        Assert.Equal(EventWeights.BrowsingSkipFactor, EventWeights.SkipFactor(source));

    [Theory]
    [InlineData(null)]
    [InlineData("search")]
    [InlineData("queue")]
    [InlineData("home:quickTiles")]
    [InlineData("something:new")]
    public void Anything_else_counts_as_before(string? source) =>
        Assert.Equal(1.0, EventWeights.SkipFactor(source));

    [Fact]
    public void Shelves_showing_the_library_are_not_recommendations()
    {
        Assert.False(PlaybackSource.IsRecommendation("home:newArrivals"));
        Assert.False(PlaybackSource.IsRecommendation("home:topTracks"));
        Assert.True(PlaybackSource.IsRecommendation("home:dailyMix"));
    }
}
