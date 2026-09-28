// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Services;
using MusicStreaming.Infrastructure.Audio;
using Xunit;

namespace MusicStreaming.UnitTests;

public class TranscodeWorkerTests
{
    [Fact]
    public async Task The_service_does_not_start_without_ffmpeg()
    {
        // До хранилищ и транскодера дело не доходит: проверка идёт первой.
        var worker = new TranscodeWorker(
            new TranscodeQueue(),
            transcoder: null!,
            storage: null!,
            hls: null!,
            Options.Create(new TranscodeOptions { FfmpegPath = "/nonexistent/ffmpeg" }),
            NullLogger<TranscodeWorker>.Instance);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => worker.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains("ffmpeg is required", failure.Message);
    }
}
