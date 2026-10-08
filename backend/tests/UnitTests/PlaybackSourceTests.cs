// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Xunit;
using Domain.Entities.Recommendations;

namespace UnitTests;

public class PlaybackSourceTests
{
    [Theory]
    [InlineData("home:forYou", "home:forYou")]
    [InlineData("radio:mood:sad", "radio:mood:sad")]
    [InlineData("  album ", "album")]
    [InlineData("mix:new_arrivals-2", "mix:new_arrivals-2")]
    public void Short_keys_are_kept(string reported, string expected) =>
        Assert.Equal(expected, PlaybackSource.Normalize(reported));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("home/forYou")]
    [InlineData("<script>")]
    [InlineData("радио")]
    public void Anything_else_is_an_unknown_source(string? reported) =>
        Assert.Null(PlaybackSource.Normalize(reported));

    [Fact]
    public void An_overlong_key_is_an_unknown_source() =>
        Assert.Null(PlaybackSource.Normalize(new string('a', PlaybackSource.MaxLength + 1)));
}
