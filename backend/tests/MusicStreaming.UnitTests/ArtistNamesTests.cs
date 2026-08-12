using MusicStreaming.Domain.Common;
using Xunit;

namespace MusicStreaming.UnitTests;

public class ArtistNamesTests
{
    [Fact]
    public void Split_returns_the_single_name_when_there_is_nothing_to_split() =>
        Assert.Equal(["Daft Punk"], ArtistNames.Split("Daft Punk"));

    [Theory]
    [InlineData("A; B")]
    [InlineData("A / B")]
    [InlineData("A, B")]
    [InlineData("A feat. B")]
    [InlineData("A featuring B")]
    [InlineData("A ft B")]
    [InlineData("A vs. B")]
    [InlineData("A x B")]
    public void Split_recognises_every_separator(string raw) =>
        Assert.Equal(["A", "B"], ArtistNames.Split(raw));

    [Fact]
    public void Split_drops_duplicates_that_differ_only_by_case_or_spacing() =>
        Assert.Equal(["Kraftwerk"], ArtistNames.Split("Kraftwerk, kraftwerk,  KRAFTWERK "));

    [Fact]
    public void Split_returns_nothing_for_blank_input() =>
        Assert.Empty(ArtistNames.Split("   "));

    [Fact]
    public void Split_caps_the_number_of_credits() =>
        Assert.Equal(
            ArtistNames.MaxCredits,
            ArtistNames.Split(string.Join(", ", Enumerable.Range(1, 40).Select(n => $"Artist {n}"))).Count);

    [Fact]
    public void SplitAll_merges_sources_without_repeating_a_name() =>
        Assert.Equal(["A", "B", "C"], ArtistNames.SplitAll(["A, B", "b; C", null]));
}
