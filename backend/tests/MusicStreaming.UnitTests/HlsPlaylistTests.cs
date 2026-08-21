// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Services;
using MusicStreaming.Domain.Common;
using Xunit;

namespace MusicStreaming.UnitTests;

public class HlsPlaylistTests
{
    [Fact]
    public void Master_contains_only_the_ready_variants_in_order()
    {
        var playlist = HlsPlaylist.BuildMaster([
            (AudioQuality.Low, 64),
            (AudioQuality.Normal, 128),
        ]);

        Assert.Contains("#EXT-X-VERSION:7", playlist);
        Assert.Contains("BANDWIDTH=64000,AVERAGE-BANDWIDTH=64000,CODECS=\"mp4a.40.2\"", playlist);
        Assert.Contains("low/index.m3u8", playlist);
        Assert.Contains("normal/index.m3u8", playlist);
        Assert.DoesNotContain("high/index.m3u8", playlist);
    }

    [Theory]
    [InlineData("index.m3u8")]
    [InlineData("init.mp4")]
    [InlineData("segment-00001.m4s")]
    public void Known_asset_names_are_accepted(string fileName) =>
        Assert.True(HlsPlaylist.IsAssetFileName(fileName));

    [Theory]
    [InlineData("../music/secret.mp3")]
    [InlineData("segment-.m4s")]
    [InlineData("segment-1.m4s/other")]
    [InlineData("master.m3u8")]
    public void Unknown_or_unsafe_asset_names_are_rejected(string fileName) =>
        Assert.False(HlsPlaylist.IsAssetFileName(fileName));
}
