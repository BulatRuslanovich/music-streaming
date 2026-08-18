using MusicStreaming.Application.Common;
using Xunit;

namespace MusicStreaming.UnitTests;

public class SearchTermTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Pattern_is_null_when_there_is_nothing_to_match(string? query) =>
        Assert.Null(Pattern(query));

    [Theory]
    [InlineData("Daft Punk", "%daft punk%")]
    [InlineData("  DAFT   punk ", "%daft punk%")]
    public void Pattern_wraps_the_normalised_term(string query, string expected) =>
        Assert.Equal(expected, Pattern(query));

    [Theory]
    [InlineData("50%", "%50\\%%")]
    [InlineData("a_b", "%a\\_b%")]
    [InlineData("back\\slash", "%back\\\\slash%")]
    public void Pattern_escapes_wildcards_so_they_match_literally(string query, string expected) =>
        Assert.Equal(expected, Pattern(query));

    [Fact]
    public void Pattern_escapes_the_escape_character_before_the_wildcards()
    {
        Assert.Equal("%\\\\\\%%", Pattern("\\%"));
    }

    [Fact]
    public void Value_keeps_the_term_unescaped_for_ranking()
    {
        Assert.Equal("50%", SearchTerm.For("  50%  ")!.Value.Value);
    }

    private static string? Pattern(string? query) => SearchTerm.For(query)?.Pattern;
}
