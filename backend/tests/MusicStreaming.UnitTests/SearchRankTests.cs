// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Common;
using Xunit;

namespace MusicStreaming.UnitTests;

/// <summary>
/// <see cref="SearchRank.Evaluate"/> обязана повторять SQL-функцию <c>search_rank</c>: ярусы здесь
/// перечислены ровно в том порядке, в котором их различает CASE в миграции.
/// </summary>
public class SearchRankTests
{
    [Theory]
    [InlineData("nirvana", "nirvana", SearchRank.Exact)]
    [InlineData("nirvana unplugged", "nirvana", SearchRank.Prefix)]
    [InlineData("the nirvana story", "nirvana", SearchRank.WordPrefix)]
    [InlineData("supernirvana", "nirvana", SearchRank.Contains)]
    [InlineData("pearl jam", "nirvana", SearchRank.Unrelated)]
    public void Each_tier_is_recognised(string value, string term, int expected) =>
        Assert.Equal(expected, SearchRank.Evaluate(value, term));

    [Fact]
    public void A_stronger_tier_always_wins_over_a_weaker_one() =>
        Assert.True(
            SearchRank.Evaluate("nirvana", "nirvana")
            < SearchRank.Evaluate("nirvana live", "nirvana"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Missing_values_are_unrelated_rather_than_a_crash(string? value) =>
        Assert.Equal(SearchRank.Unrelated, SearchRank.Evaluate(value, "nirvana"));

    [Fact]
    public void An_empty_term_matches_nothing() =>
        Assert.Equal(SearchRank.Unrelated, SearchRank.Evaluate("nirvana", string.Empty));
}
