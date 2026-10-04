// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Infrastructure.Storage;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using App.Services;
using Infrastructure.Persistence;
using App.Recommendations.Embeddings;
using Domain.Entities.Recommendations;

namespace Infrastructure.Audio;

public class AudioEmbeddingWorker(
    IServiceScopeFactory scopeFactory,
    AudioEmbeddingQueue queue,
    ClapAudioEmbedder embedder,
    FileSystemMusicStorage storage,
    EmbeddingIndex index,
    TimeProvider clock,
    ILogger<AudioEmbeddingWorker> logger) : BackgroundService
{
    private const int ReloadEvery = 64;

    private int _sinceReload;

    private const int BackfillBatchSize = 4;

    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(30);

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        embedder.EnsureLoaded();

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var backfill = BackfillAsync(stoppingToken);

        await foreach (var trackId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await EmbedAsync(trackId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Embedding of track {TrackId} failed unexpectedly", trackId);
            }
            finally
            {
                queue.MarkFinished(trackId);
            }
        }

        await backfill;
    }

    private async Task BackfillAsync(CancellationToken ct)
    {
        var modelId = embedder.ModelId;

        while (!ct.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var retryBefore = clock.GetUtcNow().AddDays(-7);

            var trackIds = await db.Tracks.AsNoTracking()
                .Where(track => track.Embedding == null
                                || track.Embedding.ModelId != modelId
                                || track.Embedding.Strategy != ClapWindowPlanner.Strategy
                                || track.Embedding.SourceHash != track.ContentHash
                                || (!track.Embedding.Succeeded && track.Embedding.AnalyzedAt <= retryBefore))
                .OrderByDescending(track => track.Stats == null ? 0 : track.Stats.PopularityScore)
                .ThenByDescending(track => track.CreatedAt)
                .Take(BackfillBatchSize * 16)
                .Select(track => track.Id)
                .ToListAsync(ct);

            foreach (var trackId in trackIds)
                queue.TryEnqueue(trackId);

            await Task.Delay(Poll, ct);
        }
    }

    private async Task EmbedAsync(Guid trackId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var track = await db.Tracks.AsNoTracking()
            .Where(item => item.Id == trackId)
            .Select(item => new { item.Id, item.FilePath, item.ContentHash, item.DurationSeconds })
            .FirstOrDefaultAsync(ct);

        if (track is null)
            return;

        var startedAt = Stopwatch.GetTimestamp();
        var source = storage.ResolveExisting(track.FilePath);

        var embedding = source is null
            ? null
            : await embedder.EmbedAsync(source, track.DurationSeconds, ct);

        var existing = await db.TrackEmbeddings.FirstOrDefaultAsync(item => item.TrackId == trackId, ct);
        var entity = existing ?? new TrackEmbedding { TrackId = trackId };

        if (existing is null)
            db.TrackEmbeddings.Add(entity);

        entity.ModelId = embedder.ModelId;
        entity.Strategy = embedder.Strategy;
        entity.SourceHash = track.ContentHash;
        entity.AnalyzedAt = clock.GetUtcNow();
        entity.Succeeded = embedding is not null;
        entity.Error = embedding is null ? (source is null ? "source_missing" : "embedding_failed") : null;

        if (embedding is not null)
        {
            entity.Vector = embedding.Vector;
            entity.Dimension = embedding.Vector.Length;
        }
        else
        {
            entity.Vector = [];
            entity.Dimension = 0;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Embedding of track {TrackId} {Result} in {Elapsed:0.0} s",
            trackId,
            entity.Succeeded ? "succeeded" : "failed",
            Stopwatch.GetElapsedTime(startedAt).TotalSeconds);

        if (!entity.Succeeded)
            return;

        if (Interlocked.Increment(ref _sinceReload) % ReloadEvery == 0)
            index.RequestReload();
    }
}
