using MusicStreaming.Application.Common;
using Xunit;

namespace MusicStreaming.UnitTests;

public class AudioUploadTests
{
    [Theory]
    [InlineData("song.mp3", ".mp3", "audio/mpeg")]
    [InlineData("song.flac", ".flac", "audio/flac")]
    [InlineData("song.m4a", ".m4a", "audio/mp4")]
    public void For_maps_a_known_extension_to_its_format(string fileName, string extension, string mimeType)
    {
        var format = Assert.NotNull(AudioUpload.For(fileName));

        Assert.Equal(extension, format.Extension);
        Assert.Equal(mimeType, format.MimeType);
    }

    [Theory]
    [InlineData("SONG.MP3")]
    [InlineData("Song.Flac")]
    [InlineData("song.M4a")]
    public void For_ignores_the_case_of_the_extension(string fileName) =>
        Assert.NotNull(AudioUpload.For(fileName));

    [Theory]
    [InlineData("song.wav")]
    [InlineData("song.ogg")]
    [InlineData("song.mp3.txt")]
    [InlineData("song")]
    [InlineData("")]
    [InlineData(".mp3.")]
    public void For_refuses_anything_else(string fileName) =>
        Assert.Null(AudioUpload.For(fileName));

    [Fact]
    public void Every_format_carries_a_leading_dot_and_a_taglib_alias()
    {
        foreach (var fileName in new[] { "a.mp3", "a.flac", "a.m4a" })
        {
            var format = Assert.NotNull(AudioUpload.For(fileName));

            Assert.StartsWith(".", format.Extension);
            Assert.StartsWith("taglib/", format.TagLibMimeType);
            Assert.Equal(format.Extension[1..].ToUpperInvariant(), format.Label);
        }
    }

    [Fact]
    public void Accepted_names_every_format_so_the_refusal_message_stays_truthful()
    {
        Assert.Contains(".mp3", AudioUpload.Accepted);
        Assert.Contains(".flac", AudioUpload.Accepted);
        Assert.Contains(".m4a", AudioUpload.Accepted);
    }

    [Fact]
    public void SniffContainer_recognises_flac_and_mp4_and_shrugs_at_everything_else()
    {
        Assert.Equal(".flac", Sniff([.. "fLaC"u8, .. new byte[16]]));

        Assert.Equal(".m4a", Sniff([0x00, 0x00, 0x00, 0x1C, .. "ftyp"u8, .. "M4A "u8]));

        Assert.Null(Sniff([.. "ID3"u8, .. new byte[16]]));
        Assert.Null(Sniff([0xFF, 0xFB, 0x90, 0x00, .. new byte[16]]));

        Assert.Null(Sniff([0x00, 0x01]));
    }

    private static string? Sniff(byte[] head)
    {
        var path = Path.Combine(Path.GetTempPath(), $"caimack-sniff-{Guid.CreateVersion7():N}");

        try
        {
            File.WriteAllBytes(path, head);
            return AudioUpload.SniffContainer(path);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
