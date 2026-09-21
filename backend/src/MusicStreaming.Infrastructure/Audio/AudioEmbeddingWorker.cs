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
using MusicStreaming.Infrastructure.Recommendations;

namespace MusicStreaming.Infrastructure.Audio;

/// <summary>
/// Считает векторы звучания для библиотеки.
/// <para>
/// Проход по треку — секунды, а не миллисекунды: на 50 тысяч треков это порядка суток на
/// четырёх ядрах. Поэтому вся система обязана оставаться корректной, пока дозаполнение ещё
/// идёт, а сам воркер должен уступать дорогу тому, что слышит слушатель.
/// </para>
/// </summary>
public class AudioEmbeddingWorker(
    IServiceScopeFactory scopeFactory,
    AudioEmbeddingQueue queue,
    IAudioEmbedder embedder,
    IMusicStorage storage,
    EmbeddingIndex index,
    IOptions<AudioEmbeddingOptions> options,
    TimeProvider clock,
    ILogger<AudioEmbeddingWorker> logger) : BackgroundService
{
    /// <summary>Через сколько успешных расчётов просить индекс перечитаться.</summary>
    private const int ReloadEvery = 64;

    private int _sinceReload;

    private AudioEmbeddingOptions Options => options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Options.Enabled || !embedder.IsAvailable)
            return;

        await Task.WhenAll(DrainQueueAsync(stoppingToken), BackfillAsync(stoppingToken));
    }

    private Task DrainQueueAsync(CancellationToken ct) =>
        queue.ConsumeAsync(
            EmbedAsync,
            (trackId, ex) => logger.LogError(ex, "Embedding of track {TrackId} failed unexpectedly", trackId),
            ct);

    private async Task BackfillAsync(CancellationToken ct)
    {
        var poll = TimeSpan.FromSeconds(Options.PollSeconds);

        while (!ct.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var retryBefore = clock.GetUtcNow().AddDays(-7);

            var trackIds = await db.Tracks.AsNoTracking()
                .Where(track => track.Embedding == null
                                || track.Embedding.ModelId != Options.ModelId
                                || track.Embedding.Strategy != ClapWindowPlanner.Strategy
                                || track.Embedding.SourceHash != track.ContentHash
                                || (!track.Embedding.Succeeded && track.Embedding.AnalyzedAt <= retryBefore))
                // По популярности, а не по дате добавления: за сутки дозаполнения слушатель
                // встретит в рекомендациях сначала то, что и так слушают, поэтому переходный
                // период ощущается на хвосте библиотеки, а не на её голове.
                .OrderByDescending(track => track.Stats == null ? 0 : track.Stats.PopularityScore)
                .ThenByDescending(track => track.CreatedAt)
                .Take(Options.BackfillBatchSize * 16)
                .Select(track => track.Id)
                .ToListAsync(ct);

            foreach (var trackId in trackIds)
                queue.TryEnqueue(trackId);

            await Task.Delay(poll, ct);
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

        // Индекс перечитывается сам раз в четверть часа; здесь его торопят, чтобы идущее
        // дозаполнение становилось видно за пачку, а не за пятнадцать минут.
        if (Interlocked.Increment(ref _sinceReload) % ReloadEvery == 0)
            index.RequestReload();
    }
}
