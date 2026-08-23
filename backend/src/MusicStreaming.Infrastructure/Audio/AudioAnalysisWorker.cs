// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Services;
using MusicStreaming.Domain.Entities.Recommendations;
using MusicStreaming.Infrastructure.Persistence;

namespace MusicStreaming.Infrastructure.Audio;

public class AudioAnalysisWorker(
    IServiceScopeFactory scopeFactory,
    AudioAnalysisQueue queue,
    IAudioFeatureAnalyzer analyzer,
    IMusicStorage storage,
    IOptions<AudioAnalysisOptions> options,
    TimeProvider clock,
    ILogger<AudioAnalysisWorker> logger) : BackgroundService
{
    public const int AlgorithmVersion = 1;

    private AudioAnalysisOptions Options => options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Options.Enabled || !analyzer.IsAvailable)
            return;

        await Task.WhenAll(DrainQueueAsync(stoppingToken), BackfillAsync(stoppingToken));
    }

    private Task DrainQueueAsync(CancellationToken ct) =>
        queue.ConsumeAsync(
            AnalyzeAsync,
            (trackId, ex) =>
                logger.LogError(ex, "Audio analysis of track {TrackId} failed unexpectedly", trackId),
            ct);

    private async Task BackfillAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var retryBefore = clock.GetUtcNow().AddDays(-7);

            var trackIds = await db.Tracks.AsNoTracking()
                .Where(track => track.AudioFeatures == null
                                || track.AudioFeatures.AlgorithmVersion < AlgorithmVersion
                                || (!track.AudioFeatures.Succeeded && track.AudioFeatures.AnalyzedAt <= retryBefore))
                .OrderBy(track => track.CreatedAt)
                .Take(Options.BackfillBatchSize * 16)
                .Select(track => track.Id)
                .ToListAsync(ct);

            foreach (var trackId in trackIds)
                queue.TryEnqueue(trackId);

            await Task.Delay(TimeSpan.FromSeconds(Options.PollSeconds), ct);
        }
    }

    private async Task AnalyzeAsync(Guid trackId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var track = await db.Tracks.AsNoTracking()
            .Where(item => item.Id == trackId)
            .Select(item => new { item.Id, item.FilePath })
            .FirstOrDefaultAsync(ct);

        if (track is null)
            return;

        var startedAt = Stopwatch.GetTimestamp();
        var source = storage.ResolveExisting(track.FilePath);
        var vector = source is null ? null : await analyzer.AnalyzeAsync(source, ct);
        var existing = await db.TrackAudioFeatures.FirstOrDefaultAsync(item => item.TrackId == trackId, ct);
        var entity = existing ?? new TrackAudioFeatures { TrackId = trackId };

        if (existing is null)
            db.TrackAudioFeatures.Add(entity);

        entity.AlgorithmVersion = AlgorithmVersion;
        entity.AnalyzedAt = clock.GetUtcNow();
        entity.Succeeded = vector is not null;
        entity.Error = vector is null ? (source is null ? "source_missing" : "analysis_failed") : null;

        if (vector is not null)
        {
            entity.TempoBpm = vector.TempoBpm;
            entity.TempoConfidence = vector.TempoConfidence;
            entity.Energy = vector.Energy;
            entity.LoudnessDb = vector.LoudnessDb;
            entity.Brightness = vector.Brightness;
            entity.DynamicRangeDb = vector.DynamicRangeDb;
            entity.AnalyzedSeconds = vector.AnalyzedSeconds;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Audio analysis of track {TrackId} {Result} in {Elapsed:0.0} s",
            trackId,
            entity.Succeeded ? "succeeded" : "failed",
            Stopwatch.GetElapsedTime(startedAt).TotalSeconds);
    }
}
