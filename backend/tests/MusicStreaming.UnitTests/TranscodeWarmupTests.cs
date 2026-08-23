// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Services;
using MusicStreaming.Domain.Common;
using Xunit;

namespace MusicStreaming.UnitTests;

public class TranscodeWarmupTests
{
    [Fact]
    public void A_warm_track_has_both_an_opus_and_an_hls_rendition_of_every_warmed_quality()
    {
        var requests = TranscodeWarmup.For("hash", "music/aa/bb/track.flac").ToList();

        Assert.Equal(4, requests.Count);
        Assert.Distinct(requests.Select(request => request.Key));

        foreach (var quality in new[] { AudioQuality.Low, AudioQuality.Normal })
        {
            foreach (var kind in new[] { TranscodeKind.Opus, TranscodeKind.Hls })
                Assert.Contains(requests, request => request.Quality == quality && request.Kind == kind);
        }
    }

    [Fact]
    public void The_heavy_quality_is_left_to_be_prepared_on_demand()
    {
        var requests = TranscodeWarmup.For("hash", "music/aa/bb/track.flac");

        Assert.DoesNotContain(requests, request => request.Quality == AudioQuality.High);
        Assert.DoesNotContain(requests, request => request.Quality == AudioQuality.Original);
    }

    [Fact]
    public void Renditions_already_on_disk_are_not_queued_again()
    {
        var onDisk = new HashSet<string>(
            [
                new TranscodeRequest("first", "a.flac", AudioQuality.Low).Key,
                new TranscodeRequest("first", "a.flac", AudioQuality.Normal, TranscodeKind.Hls).Key,
            ],
            StringComparer.Ordinal);

        var missing = TranscodeWarmup.Missing(
            [("first", "a.flac")],
            request => onDisk.Contains(request.Key));

        Assert.Equal(2, missing.Count);
        Assert.DoesNotContain(missing, request => onDisk.Contains(request.Key));
    }

    [Fact]
    public void A_fully_warmed_library_leaves_nothing_to_do()
    {
        var missing = TranscodeWarmup.Missing(
            [("first", "a.flac"), ("second", "b.mp3")],
            _ => true);

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_track_of_a_cold_library_is_planned()
    {
        var missing = TranscodeWarmup.Missing(
            [("first", "a.flac"), ("second", "b.mp3")],
            _ => false);

        Assert.Equal(8, missing.Count);
        Assert.Distinct(missing.Select(request => request.Key));
    }
}
