// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Services;
using MusicStreaming.Infrastructure.Persistence;

namespace MusicStreaming.Infrastructure.Audio;

public class TranscodeBackfillService(
    IServiceScopeFactory scopeFactory,
    TranscodeQueue queue,
    IAudioTranscoder transcoder,
    IMusicStorage storage,
    IOptions<TranscodeOptions> options,
    ILogger<TranscodeBackfillService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (!settings.Enabled || !settings.BackfillEnabled || !transcoder.IsAvailable)
            return;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(settings.BackfillStartupDelaySeconds), stoppingToken);
            await BackfillAsync(settings, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Transcode backfill stopped unexpectedly");
        }
    }

    private async Task BackfillAsync(TranscodeOptions settings, CancellationToken ct)
    {
        var pending = await FindMissingAsync(ct);
        if (pending.Count == 0)
            return;

        logger.LogInformation(
            "Warming {Count} missing renditions across {Tracks} tracks",
            pending.Count,
            pending.Select(request => request.ContentHash).Distinct(StringComparer.Ordinal).Count());

        var pause = TimeSpan.FromSeconds(settings.BackfillPauseSeconds);
        var queued = 0;
        var skipped = 0;

        var remaining = new Queue<TranscodeRequest>(pending);

        while (remaining.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var carried = new List<TranscodeRequest>();

            for (var slot = 0; slot < settings.BackfillBatchSize && remaining.Count > 0; slot++)
            {
                var request = remaining.Dequeue();

                if (AlreadyOnDisk(request))
                {
                    skipped++;
                    continue;
                }

                if (queue.TryEnqueueWarmup(request))
                    queued++;
                else
                    carried.Add(request);
            }

            foreach (var request in carried)
                remaining.Enqueue(request);

            if (remaining.Count > 0)
                await Task.Delay(pause, ct);
        }

        logger.LogInformation(
            "Transcode backfill finished: {Queued} renditions queued, {Skipped} already on disk",
            queued, skipped);
    }

    private async Task<IReadOnlyList<TranscodeRequest>> FindMissingAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tracks = await db.Tracks.AsNoTracking()
            .Select(track => new { track.ContentHash, track.FilePath })
            .Distinct()
            .ToListAsync(ct);

        return TranscodeWarmup.Missing(
            tracks.Select(track => (track.ContentHash, track.FilePath)),
            AlreadyOnDisk);
    }

    private bool AlreadyOnDisk(TranscodeRequest request) =>
        request.Kind == TranscodeKind.Hls
            ? storage.HlsVariantReady(request.ContentHash, request.Quality)
            : storage.ResolveExisting(
                storage.TranscodePathFor(request.ContentHash, request.Quality)) is not null;
}
