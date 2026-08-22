// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Infrastructure.Audio;
using Xunit;

namespace MusicStreaming.UnitTests;

public class FfmpegProcessTests
{
    [Fact]
    public void Start_info_preserves_the_executable_and_argument_order()
    {
        var startInfo = FfmpegProcess.CreateStartInfo(
            "/opt/media tools/ffmpeg",
            ["-hide_banner", "-i", "/music/a song.flac", "pipe:1"]);

        Assert.Equal("/opt/media tools/ffmpeg", startInfo.FileName);
        Assert.Equal(
            ["-hide_banner", "-i", "/music/a song.flac", "pipe:1"],
            startInfo.ArgumentList);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
    }
}
