// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Services;
using MusicStreaming.Infrastructure.Persistence;

namespace MusicStreaming.Infrastructure.Audio;

public class TranscodeBackfillService(
    IServiceScopeFactory scopeFactory,
    TranscodeQueue queue,
    IHlsStorage hls,
    ILogger<TranscodeBackfillService> logger) : ScheduledWorker(scopeFactory, logger)
{
    private static readonly TimeSpan Startup = TimeSpan.FromSeconds(30);

    private const int BatchSize = 8;

    private static readonly TimeSpan Pause = TimeSpan.FromSeconds(5);

    protected override TimeSpan StartupDelay => Startup;
    protected override TimeSpan? Interval => null;
    protected override string Name => "Transcode backfill";

    protected override async Task RunPassAsync(CancellationToken ct)
    {
        IReadOnlyList<TranscodeRequest> pending;
        using (var scope = CreateScope())
        {
            var tracks = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Tracks.AsNoTracking()
                .Select(track => new { track.ContentHash, track.FilePath })
                .Distinct()
                .ToListAsync(ct);

            pending = TranscodeWarmup.Missing(tracks.Select(track => (track.ContentHash, track.FilePath)), AlreadyOnDisk);
        }

        if (pending.Count == 0)
            return;

        logger.LogInformation(
            "Warming {Count} missing renditions across {Tracks} tracks",
            pending.Count,
            pending.Select(request => request.ContentHash).Distinct(StringComparer.Ordinal).Count());

        var queued = 0;
        var skipped = 0;

        var remaining = new Queue<TranscodeRequest>(pending);

        while (remaining.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var carried = new List<TranscodeRequest>();

            for (var slot = 0; slot < BatchSize && remaining.Count > 0; slot++)
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
                await Task.Delay(Pause, ct);
        }

        logger.LogInformation(
            "Transcode backfill finished: {Queued} renditions queued, {Skipped} already on disk",
            queued, skipped);
    }

    private bool AlreadyOnDisk(TranscodeRequest request) =>
        hls.HlsVariantReady(request.ContentHash, request.Quality);
}
