// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class AudioFormatTests(RecommendationApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task A_flac_file_becomes_a_track_with_its_tags_and_audio_properties()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = TrackUploadTestClient.UniqueName("Flac");

        var result = await TrackUploadTestClient.UploadAsync(client, [
            SyntheticFlac.Tagged($"{name}.flac", $"{name} Title", $"{name} Artist", $"{name} Album"),
        ], Json);

        Assert.Empty(result.Failed);
        var uploaded = Assert.Single(result.Uploaded);

        Assert.Equal($"{name} Title", uploaded.Title);
        Assert.Equal($"{name} Artist", uploaded.ArtistName);
        Assert.Equal($"{name} Album", uploaded.AlbumTitle);
        Assert.Equal("flac", uploaded.Codec);
        Assert.Equal(16, uploaded.BitsPerSample);
        Assert.Equal(44100, uploaded.SampleRateHz);
        Assert.True(uploaded.DurationSeconds > 0, "STREAMINFO carries the duration even without frames");

        await AssertStoredAsAsync(uploaded.Id, ".flac", "audio/flac");
    }

    [Theory]
    [InlineData("alac.m4a", "alac")]
    [InlineData("aac.m4a", "aac")]
    public async Task An_m4a_file_records_the_codec_inside_its_container(string fixtureName, string expectedCodec)
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = TrackUploadTestClient.UniqueName(expectedCodec);

        var result = await TrackUploadTestClient.UploadAsync(
            client, [Fixture($"{name}.m4a", fixtureName)], Json);

        Assert.Empty(result.Failed);
        var uploaded = Assert.Single(result.Uploaded);

        Assert.Equal(expectedCodec, uploaded.Codec);
        await AssertStoredAsAsync(uploaded.Id, ".m4a", "audio/mp4");
    }

    [Fact]
    public async Task An_unsupported_extension_is_refused()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = TrackUploadTestClient.UniqueName("Wav");

        var result = await TrackUploadTestClient.UploadAsync(client, [
            new TestUploadFile($"{name}.wav", "audio/wav", [0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00]),
        ], Json);

        Assert.Empty(result.Uploaded);
        var failure = Assert.Single(result.Failed);
        Assert.Contains(".flac", failure.Reason);
    }

    [Theory]
    [InlineData("mp3", "flac")]
    [InlineData("mp3", "m4a")]
    [InlineData("flac", "mp3")]
    [InlineData("flac", "m4a")]
    [InlineData("m4a", "mp3")]
    [InlineData("m4a", "flac")]
    public async Task A_file_renamed_to_another_format_is_refused(string actual, string claimed)
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = TrackUploadTestClient.UniqueName("Liar");

        var content = actual switch
        {
            "mp3" => SyntheticMp3.Tagged($"{name}.mp3", $"{name} Title", $"{name} Artist", null, null, null, 1).Content,
            "flac" => SyntheticFlac.Tagged($"{name}.flac", $"{name} Title", $"{name} Artist", null).Content,
            _ => Fixture($"{name}.m4a", "aac.m4a").Content,
        };

        var result = await TrackUploadTestClient.UploadAsync(
            client, [new TestUploadFile($"{name}.{claimed}", null, content)], Json);

        Assert.Empty(result.Uploaded);
        Assert.Single(result.Failed);
    }

    [Fact]
    public async Task A_flac_file_is_accepted_whatever_content_type_the_browser_claims()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = TrackUploadTestClient.UniqueName("Claimed");

        var tagged = SyntheticFlac.Tagged($"{name}.flac", $"{name} Title", $"{name} Artist", null);
        var result = await TrackUploadTestClient.UploadAsync(
            client, [tagged with { ContentType = "video/mp4" }], Json);

        Assert.Empty(result.Failed);
        Assert.Single(result.Uploaded);
    }

    private async Task AssertStoredAsAsync(Guid trackId, string extension, string mimeType)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var track = await db.Tracks.AsNoTracking()
            .Where(t => t.Id == trackId)
            .Select(t => new { t.FilePath, t.MimeType })
            .FirstAsync(Cancel.Token);

        Assert.EndsWith(extension, track.FilePath);
        Assert.Equal(mimeType, track.MimeType);
    }

    private static TestUploadFile Fixture(string fileName, string fixtureName) =>
        new(fileName, "audio/mp4",
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureName)));

    internal static class SyntheticFlac
    {
        private const int SampleRate = 44100;
        private const int Channels = 2;
        private const int BitsPerSample = 16;
        private const int Seconds = 3;

        public static TestUploadFile Tagged(string fileName, string title, string artist, string? album)
        {
            var path = Path.Combine(Path.GetTempPath(), $"caimack-upload-{Guid.CreateVersion7():N}.flac");

            try
            {
                File.WriteAllBytes(path, Skeleton());

                using (var tagged = TagLib.File.Create(path, "taglib/flac", TagLib.ReadStyle.Average))
                {
                    tagged.Tag.Title = title;
                    tagged.Tag.Performers = [artist];

                    if (album is not null)
                        tagged.Tag.Album = album;

                    tagged.Save();
                }

                return new TestUploadFile(fileName, "audio/flac", File.ReadAllBytes(path));
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private static byte[] Skeleton()
        {
            var streamInfo = new byte[34];

            streamInfo[0] = 0x10;
            streamInfo[2] = 0x10;

            var packed = ((ulong)SampleRate << 44)
                | ((ulong)(Channels - 1) << 41)
                | ((ulong)(BitsPerSample - 1) << 36)
                | ((ulong)(SampleRate * Seconds) & 0xF_FFFF_FFFF);

            for (var i = 0; i < 8; i++)
                streamInfo[10 + i] = (byte)(packed >> (56 - i * 8));

            List<byte> file = [.. "fLaC"u8];

            file.Add(0x80);
            file.AddRange([0x00, 0x00, 0x22]);
            file.AddRange(streamInfo);
            file.AddRange(new byte[2048]);

            return [.. file];
        }
    }

    internal static class SyntheticMp3
    {
        private static readonly byte[] FrameHeader = [0xFF, 0xFB, 0x90, 0x00];
        private const int FrameLength = 417;
        private const int FrameCount = 120;

        public static TestUploadFile Tagged(
            string fileName, string title, string artist, string? album, string? genre, int? year, int? track)
        {
            var path = Path.Combine(Path.GetTempPath(), $"caimack-upload-{Guid.CreateVersion7():N}.mp3");

            try
            {
                var audio = new byte[FrameLength * FrameCount];
                for (var frame = 0; frame < FrameCount; frame++)
                    FrameHeader.CopyTo(audio, frame * FrameLength);

                File.WriteAllBytes(path, audio);

                using (var tagged = TagLib.File.Create(path, "taglib/mp3", TagLib.ReadStyle.Average))
                {
                    tagged.Tag.Title = title;
                    tagged.Tag.Performers = [artist];

                    if (album is not null) tagged.Tag.Album = album;
                    if (genre is not null) tagged.Tag.Genres = [genre];
                    if (year is { } value) tagged.Tag.Year = (uint)value;
                    if (track is { } number) tagged.Tag.Track = (uint)number;

                    tagged.Save();
                }

                return new TestUploadFile(fileName, "audio/mpeg", File.ReadAllBytes(path));
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }
}
