// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Services;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Infrastructure.Audio;

public class TranscodeWorker(
    TranscodeQueue queue,
    IAudioTranscoder transcoder,
    IMusicStorage storage,
    IHlsStorage hls,
    ILogger<TranscodeWorker> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, byte> _running = new(StringComparer.Ordinal);

    private static int Workers => Math.Max(1, Environment.ProcessorCount / 2);

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (!FfmpegProcess.IsPresent(FfmpegProcess.Executable, logger))
            throw new InvalidOperationException("ffmpeg is required but could not be started. Install it on PATH.");

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var warmupWorkers = Math.Max(1, Workers - 1);

        var workers = new List<Task> { WorkAsync(queue.ReadUrgentAsync(stoppingToken), stoppingToken) };
        for (var worker = 0; worker < warmupWorkers; worker++)
            workers.Add(WorkAsync(queue.ReadWarmupAsync(stoppingToken), stoppingToken));

        logger.LogInformation(
            "Transcode worker started: 1 urgent worker, {WarmupWorkers} warmup workers",
            warmupWorkers);

        await Task.WhenAll(workers);
    }

    private async Task WorkAsync(IAsyncEnumerable<TranscodeRequest> requests, CancellationToken ct)
    {
        await foreach (var request in requests)
        {
            if (!_running.TryAdd(request.Key, 0))
                continue;

            try
            {
                if (AudioBitrates.For(request.Quality) is not { } bitrate)
                    continue;

                if (storage.ResolveExisting(request.SourceRelativePath) is not { } source)
                {
                    logger.LogWarning(
                        "Skipped transcoding {Key}: {Path} is missing from storage",
                        request.Key, request.SourceRelativePath);
                    continue;
                }

                if (hls.HlsVariantReady(request.ContentHash, request.Quality))
                    continue;

                var startedAt = Stopwatch.GetTimestamp();
                var target = hls.EnsureHlsVariantDirectory(request.ContentHash, request.Quality);

                if (await transcoder.TranscodeToHlsAsync(source, target, bitrate, ct))
                {
                    logger.LogInformation(
                        "Prepared the {Quality} HLS rendition of {Hash} in {Elapsed:0.0} s",
                        request.Quality,
                        request.ContentHash,
                        Stopwatch.GetElapsedTime(startedAt).TotalSeconds);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Transcoding {Key} failed unexpectedly", request.Key);
            }
            finally
            {
                _running.TryRemove(request.Key, out _);
            }
        }
    }
}
