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
    // Одна вариация может стоять сразу в обеих полосах. Перекодирует тот воркер, что взял её
    // первым, второй пропускает: иначе два ffmpeg писали бы в один и тот же каталог.
    private readonly ConcurrentDictionary<string, byte> _running = new(StringComparer.Ordinal);

    // ffmpeg здесь запускается с -threads 1, поэтому пропускную способность даёт число
    // параллельных заданий, а не потоков внутри одного. Половина ядер — чтобы остался запас на
    // API. В контейнере ProcessorCount уже учитывает лимиты cgroup.
    private static int Workers => Math.Max(1, Environment.ProcessorCount / 2);

    // Без ffmpeg нет HLS, а без HLS плеер не умеет ни понижать качество, ни играть ALAC. Лучше
    // не подняться вовсе, чем молча работать вполсилы: так отсутствие ffmpeg видно сразу.
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (!FfmpegProcess.IsPresent(FfmpegProcess.Executable, logger))
            throw new InvalidOperationException("ffmpeg is required but could not be started. Install it on PATH.");

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Один воркер закреплён за срочной полосой и никогда не занят прогревом: иначе трек,
        // который слушают сейчас, встаёт в хвост за сотнями фоновых вариаций. Остальные греют
        // библиотеку.
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
                await ProcessAsync(request, ct);
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

    private async Task ProcessAsync(TranscodeRequest request, CancellationToken ct)
    {
        if (AudioBitrates.For(request.Quality) is not { } bitrate)
            return;

        var source = storage.ResolveExisting(request.SourceRelativePath);
        if (source is null)
        {
            logger.LogWarning(
                "Skipped transcoding {Key}: {Path} is missing from storage",
                request.Key, request.SourceRelativePath);
            return;
        }

        if (hls.HlsVariantReady(request.ContentHash, request.Quality))
            return;

        var startedAt = Stopwatch.GetTimestamp();
        var target = hls.EnsureHlsVariantDirectory(request.ContentHash, request.Quality);

        if (!await transcoder.TranscodeToHlsAsync(source, target, bitrate, ct))
            return;

        logger.LogInformation(
            "Prepared the {Quality} HLS rendition of {Hash} in {Elapsed:0.0} s",
            request.Quality,
            request.ContentHash,
            Stopwatch.GetElapsedTime(startedAt).TotalSeconds);
    }
}
