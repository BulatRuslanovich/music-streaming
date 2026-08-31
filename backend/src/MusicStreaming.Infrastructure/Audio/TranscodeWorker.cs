// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Infrastructure.Audio;

public class TranscodeWorker(
    TranscodeQueue queue,
    IAudioTranscoder transcoder,
    IMusicStorage storage,
    IOptions<TranscodeOptions> options,
    StreamingMetrics metrics,
    ILogger<TranscodeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!transcoder.IsAvailable)
            return;

        // Один воркер закреплён за on-demand и никогда не занят прогревом: иначе трек, который
        // слушают сейчас, встаёт в хвост за сотнями фоновых рендишенов. Остальные греют библиотеку.
        var warmupWorkers = Math.Max(1, options.Value.EffectiveWorkers - 1);

        var lanes = new List<Task> { Consume(queue, stoppingToken) };
        for (var worker = 0; worker < warmupWorkers; worker++)
            lanes.Add(Consume(queue.Warmup, stoppingToken));

        logger.LogInformation(
            "Transcode worker started: 1 on-demand lane, {WarmupWorkers} warmup lanes",
            warmupWorkers);

        await Task.WhenAll(lanes);
    }

    private Task Consume(IWorkQueue<TranscodeRequest> lane, CancellationToken stoppingToken) =>
        lane.ConsumeAsync(
            ProcessAsync,
            (request, ex) =>
            {
                if (request.Kind == TranscodeKind.Hls)
                    metrics.RecordTranscode(request.Quality, TimeSpan.Zero, succeeded: false);
                logger.LogError(ex, "Transcoding {Key} failed unexpectedly", request.Key);
            },
            stoppingToken);

    private async Task ProcessAsync(TranscodeRequest request, CancellationToken ct)
    {
        if (options.Value.BitrateFor(request.Quality) is not { } bitrate)
            return;

        var source = storage.ResolveExisting(request.SourceRelativePath);
        if (source is null)
        {
            if (request.Kind == TranscodeKind.Hls)
                metrics.RecordTranscode(request.Quality, TimeSpan.Zero, succeeded: false);
            logger.LogWarning(
                "Skipped transcoding {Key}: {Path} is missing from storage",
                request.Key, request.SourceRelativePath);
            return;
        }

        var startedAt = Stopwatch.GetTimestamp();

        if (request.Kind == TranscodeKind.Hls)
        {
            if (storage.HlsVariantReady(request.ContentHash, request.Quality))
                return;

            var hlsTarget = storage.EnsureHlsVariantDirectory(request.ContentHash, request.Quality);
            var succeeded = await transcoder.TranscodeToHlsAsync(source, hlsTarget, bitrate, ct);
            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            metrics.RecordTranscode(request.Quality, elapsed, succeeded);

            if (!succeeded)
                return;

            logger.LogInformation(
                "Prepared the {Quality} HLS rendition of {Hash} in {Elapsed:0.0} s",
                request.Quality,
                request.ContentHash,
                elapsed.TotalSeconds);
            return;
        }

        var targetRelativePath = storage.TranscodePathFor(request.ContentHash, request.Quality);
        if (storage.ResolveExisting(targetRelativePath) is not null)
            return;

        var target = storage.ResolveForWrite(targetRelativePath);

        if (!await transcoder.TranscodeToOpusAsync(source, target, bitrate, ct))
            return;

        logger.LogInformation(
            "Cached the {Quality} rendition of {Hash} in {Elapsed:0.0} s: {SourceBytes} → {TargetBytes} bytes",
            request.Quality,
            request.ContentHash,
            Stopwatch.GetElapsedTime(startedAt).TotalSeconds,
            new FileInfo(source).Length,
            new FileInfo(target).Length);
    }
}
