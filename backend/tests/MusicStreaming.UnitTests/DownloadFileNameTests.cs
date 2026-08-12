using MusicStreaming.Application.Common;
using Xunit;

namespace MusicStreaming.UnitTests;

public class DownloadFileNameTests
{
    [Fact]
    public void For_joins_the_artist_and_the_title() =>
        Assert.Equal("Daft Punk - Aerodynamic.mp3", DownloadFileName.For("Daft Punk", "Aerodynamic", ".mp3"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void For_falls_back_to_the_title_alone_without_an_artist(string? artist) =>
        Assert.Equal("Aerodynamic.mp3", DownloadFileName.For(artist, "Aerodynamic", ".mp3"));

    [Fact]
    public void For_strips_characters_a_filesystem_would_reject() =>
        Assert.Equal("AC DC - Back In Black.mp3", DownloadFileName.For("AC/DC", "Back:In*Black", ".mp3"));

    [Fact]
    public void For_never_returns_a_bare_extension() =>
        Assert.Equal("track.mp3", DownloadFileName.For(null, "///", ".mp3"));

    [Fact]
    public void For_keeps_the_name_within_a_sane_length()
    {
        var name = DownloadFileName.For(new string('a', 200), new string('b', 200), ".mp3");

        Assert.EndsWith(".mp3", name);
        Assert.True(name.Length <= 124, $"expected a trimmed name, got {name.Length} characters");
    }
}
