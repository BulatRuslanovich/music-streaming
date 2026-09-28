// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Logging.Abstractions;
using MusicStreaming.Infrastructure.Audio;
using Xunit;

namespace MusicStreaming.UnitTests;

public class TranscodeWorkerTests
{
    /// <summary>
    /// TranscodeWorker отказывается стартовать ровно по этой пробе. Путь к ffmpeg — константа,
    /// поэтому проверяется сама проба, а не воркер с подменённой настройкой.
    /// </summary>
    [Fact]
    public void A_missing_ffmpeg_is_detected()
    {
        Assert.False(FfmpegProcess.IsPresent("/nonexistent/ffmpeg", NullLogger.Instance));
    }
}
