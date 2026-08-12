using MusicStreaming.Domain.Common;
using Xunit;

namespace MusicStreaming.UnitTests;

public class NormalizeTests
{
    [Theory]
    [InlineData("Daft Punk", "daft punk")]
    [InlineData("  Daft   Punk  ", "daft punk")]
    [InlineData("DAFT PUNK", "daft punk")]
    [InlineData("", "")]
    public void Key_collapses_case_and_whitespace(string input, string expected) =>
        Assert.Equal(expected, Normalize.Key(input));

    [Fact]
    public void Key_is_stable_for_values_that_only_differ_by_spacing() =>
        Assert.Equal(Normalize.Key("Pink  Floyd"), Normalize.Key(" pink floyd "));
}
