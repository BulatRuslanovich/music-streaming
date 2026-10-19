// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Diagnostics;
using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Audio;

// Размечает треки точками склейки (TrackCueAnalyzer). Проход берёт всё, что ещё не размечено,
// размечено старой версией анализатора или по другому файлу, — так новые загрузки попадают
// в разметку в пределах интервала, а смена версии пересчитывает библиотеку сама.
public class TrackCueWorker(
    IServiceScopeFactory scopeFactory,
    TrackCueAnalyzer analyzer,
    FileSystemMusicStorage storage,
    TimeProvider clock,
    ILogger<TrackCueWorker> logger) : ScheduledWorker(scopeFactory, logger)
{
    private const int BatchSize = 32;

    private static readonly TimeSpan RetryFailedAfter = TimeSpan.FromDays(7);

    protected override TimeSpan StartupDelay => TimeSpan.FromSeconds(20);
    protected override TimeSpan? Interval => TimeSpan.FromSeconds(30);
    protected override string Name => "Track cue worker";

    protected override async Task RunPassAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var scope = CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var retryBefore = clock.GetUtcNow() - RetryFailedAfter;

            var batch = await db.Tracks.AsNoTracking()
                .Where(track => track.Cues == null
                                || track.Cues.Version != TrackCueAnalyzer.Version
                                || track.Cues.SourceHash != track.ContentHash
                                || (!track.Cues.Succeeded && track.Cues.AnalyzedAt <= retryBefore))
                .OrderByDescending(track => track.CreatedAt)
                .Take(BatchSize)
                .Select(track => new { track.Id, track.FilePath, track.ContentHash })
                .ToListAsync(ct);

            if (batch.Count == 0)
                return;

            foreach (var track in batch)
            {
                var startedAt = Stopwatch.GetTimestamp();
                var source = storage.ResolveExisting(track.FilePath);
                var cues = source is null ? null : await AnalyzeSafelyAsync(source, ct);

                var entity = await db.TrackCues.FirstOrDefaultAsync(item => item.TrackId == track.Id, ct);
                if (entity is null)
                {
                    entity = new TrackCues { TrackId = track.Id };
                    db.TrackCues.Add(entity);
                }

                entity.SourceHash = track.ContentHash;
                entity.Version = TrackCueAnalyzer.Version;
                entity.Succeeded = cues is not null;
                entity.AudioStartSeconds = Round(cues?.AudioStartSeconds);
                entity.AudioEndSeconds = Round(cues?.AudioEndSeconds);
                entity.OutroStartSeconds = Round(cues?.OutroStartSeconds);
                entity.TempoBpm = cues?.TempoBpm;
                entity.AnalyzedAt = clock.GetUtcNow();

                logger.LogDebug(
                    "Cues of track {TrackId} {Result} in {Elapsed:0.0} s",
                    track.Id,
                    cues is null ? "failed" : "found",
                    Stopwatch.GetElapsedTime(startedAt).TotalSeconds);
            }

            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<TrackCuePoints?> AnalyzeSafelyAsync(string source, CancellationToken ct)
    {
        try
        {
            return await analyzer.AnalyzeAsync(source, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cue analysis of {Source} failed", source);
            return null;
        }
    }

    private static double? Round(double? seconds) => seconds is { } value ? Math.Round(value, 3) : null;
}
