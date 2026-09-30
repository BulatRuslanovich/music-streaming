// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Services;
using MusicStreaming.Domain.Common;
using Xunit;

namespace MusicStreaming.UnitTests;

public class TranscodeWarmupTests
{
    [Fact]
    public void A_warm_track_has_an_hls_rendition_of_every_warmed_quality()
    {
        var requests = TranscodeWarmup.For("hash", "music/aa/bb/track.flac", "flac", 1000).ToList();

        Assert.Equal(2, requests.Count);
        Assert.Distinct(requests.Select(request => request.Key));

        foreach (var quality in new[] { AudioQuality.Low, AudioQuality.Normal })
            Assert.Contains(requests, request => request.Quality == quality);
    }

    [Fact]
    public void The_original_is_never_a_rendition() =>
        Assert.DoesNotContain(
            TranscodeWarmup.For("hash", "music/aa/bb/track.flac", "flac", 1000),
            request => request.Quality == AudioQuality.Original);

    [Theory]
    [InlineData(320, new[] { AudioQuality.Low, AudioQuality.Normal })]
    [InlineData(192, new[] { AudioQuality.Low, AudioQuality.Normal })]
    [InlineData(128, new[] { AudioQuality.Low })]
    [InlineData(64, new AudioQuality[0])]
    public void A_lossy_track_is_only_transcoded_into_something_lighter_than_itself(
        int sourceKbps, AudioQuality[] expected) =>
        Assert.Equal(expected, TranscodeWarmup.For("hash", "a.mp3", "mp3", sourceKbps).Select(request => request.Quality));

    [Theory]
    [InlineData(AudioQuality.Normal, "mp3", 320, true)]
    [InlineData(AudioQuality.Normal, "mp3", 128, false)]
    [InlineData(AudioQuality.Normal, "aac", 96, false)]
    [InlineData(AudioQuality.Normal, "flac", 900, true)]
    [InlineData(AudioQuality.Normal, "alac", 100, true)]
    [InlineData(AudioQuality.Normal, null, null, true)]
    public void A_rendition_is_worthwhile_when_it_is_lighter_than_the_source_or_the_source_is_lossless(
        AudioQuality quality, string? codec, int? sourceKbps, bool worthwhile) =>
        Assert.Equal(worthwhile, TranscodeWarmup.Worthwhile(quality, codec, sourceKbps));

    [Fact]
    public void Renditions_already_on_disk_are_not_queued_again()
    {
        var onDisk = new HashSet<string>(
            [new TranscodeRequest("first", "a.flac", AudioQuality.Low).Key],
            StringComparer.Ordinal);

        var missing = TranscodeWarmup.Missing(
            [("first", "a.flac", "flac", 900)],
            request => onDisk.Contains(request.Key));

        Assert.Single(missing);
        Assert.DoesNotContain(missing, request => onDisk.Contains(request.Key));
    }

    [Fact]
    public void A_fully_warmed_library_leaves_nothing_to_do()
    {
        var missing = TranscodeWarmup.Missing(
            [("first", "a.flac", "flac", 900), ("second", "b.mp3", "mp3", 320)],
            _ => true);

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_track_of_a_cold_library_is_planned()
    {
        var missing = TranscodeWarmup.Missing(
            [("first", "a.flac", "flac", 900), ("second", "b.mp3", "mp3", 320)],
            _ => false);

        Assert.Equal(4, missing.Count);
        Assert.Distinct(missing.Select(request => request.Key));
    }
}
