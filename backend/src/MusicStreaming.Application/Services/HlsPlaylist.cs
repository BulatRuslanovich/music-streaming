// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Text;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Services;

public static class HlsPlaylist
{
    public const string IndexFileName = "index.m3u8";
    public const string MediaFileName = "media.m4s";

    public static string BuildMaster(IEnumerable<(AudioQuality Quality, int BitrateKbps)> variants)
    {
        var playlist = new StringBuilder()
            .AppendLine("#EXTM3U")
            .AppendLine("#EXT-X-VERSION:7")
            .AppendLine("#EXT-X-INDEPENDENT-SEGMENTS");

        foreach (var (quality, bitrateKbps) in variants)
        {
            var bitrate = bitrateKbps * 1000;
            playlist.Append("#EXT-X-STREAM-INF:BANDWIDTH=")
                .Append(bitrate)
                .Append(",AVERAGE-BANDWIDTH=")
                .Append(bitrate)
                .AppendLine(",CODECS=\"mp4a.40.2\"")
                .Append(quality.ToString().ToLowerInvariant())
                .Append('/')
                .AppendLine(IndexFileName);
        }

        return playlist.ToString();
    }

    public static bool IsAssetFileName(string fileName) => fileName is IndexFileName or MediaFileName;
}
