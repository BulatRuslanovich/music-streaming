// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using App.Abstractions;
using App.Recommendations;
using App.Recommendations.Embeddings;

namespace Infrastructure.Recommendations;

public class EmbeddingIndexLoader(
    IServiceScopeFactory scopeFactory,
    EmbeddingIndex index,
    ILogger<EmbeddingIndexLoader> logger) : ScheduledWorker(scopeFactory, logger)
{
    private int _lastCount = -1;
    private DateTimeOffset? _lastAnalyzedAt;

    protected override TimeSpan StartupDelay => TimeSpan.FromSeconds(RecommendationTuning.Maintenance.StartupDelaySeconds);
    protected override TimeSpan? Interval => TimeSpan.FromMinutes(15);
    protected override string Name => "Embedding index loader";

    protected override async Task RunPassAsync(CancellationToken ct)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var ready = db.TrackEmbeddings.AsNoTracking().Where(embedding => embedding.Succeeded);

        var count = await ready.CountAsync(ct);
        var analyzedAt = count == 0 ? null : await ready.MaxAsync(embedding => (DateTimeOffset?)embedding.AnalyzedAt, ct);

        var forced = index.ConsumeReloadRequest();
        if (!forced && count == _lastCount && analyzedAt == _lastAnalyzedAt)
            return;

        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var snapshot = await BuildAsync(db, ct);

        index.Publish(snapshot);
        _lastCount = count;
        _lastAnalyzedAt = analyzedAt;

        logger.LogInformation(
            "Embedding index rebuilt: {Count} tracks, {Dimension} dimensions, {Elapsed}ms",
            snapshot.Count,
            snapshot.Dimension,
            (int)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
    }

    private async Task<EmbeddingSnapshot> BuildAsync(IApplicationDbContext db, CancellationToken ct)
    {
        var ready = db.TrackEmbeddings
            .AsNoTracking()
            .Where(embedding => embedding.Succeeded && embedding.Track != null);

        var dimension = await ready
            .OrderBy(embedding => embedding.TrackId)
            .Select(embedding => (int?)embedding.Dimension)
            .FirstOrDefaultAsync(ct);

        if (dimension is not ({ } width and > 0))
            return EmbeddingSnapshot.Empty;

        var matching = ready.Where(embedding => embedding.Dimension == width);
        var capacity = await matching.CountAsync(ct);

        if (capacity == 0)
            return EmbeddingSnapshot.Empty;

        var matrix = new float[capacity * width];
        var meta = new TrackVectorMeta[capacity];
        var used = 0;
        var skipped = 0;

        var stream = matching
            .OrderBy(embedding => embedding.TrackId)
            .Select(embedding => new
            {
                embedding.TrackId,
                embedding.Vector,
                embedding.Track!.ArtistId,
                embedding.Track.ContentHash,
                embedding.Track.Title,
                ArtistName = embedding.Track.Artist!.Name,
                embedding.Track.CreatedAt,
                SkippedEarlyCount = embedding.Track.Stats == null ? 0 : embedding.Track.Stats.SkippedEarlyCount,
            })
            .AsAsyncEnumerable();

        await foreach (var source in stream.WithCancellation(ct))
        {
            if (source.Vector.Length != width || used == capacity)
            {
                skipped++;
                continue;
            }

            var target = matrix.AsSpan(used * width, width);
            source.Vector.CopyTo(target);

            VectorMath.NormalizeInPlace(target);

            // Ключ «той же песни» (артист|название) ловит копии трека с разных альбомов.
            var artist = source.ArtistName?.Trim();
            var title = source.Title?.Trim();
            var songKey = string.IsNullOrEmpty(artist) || string.IsNullOrEmpty(title)
                ? string.Empty
                : $"{artist.ToLowerInvariant()}|{title.ToLowerInvariant()}";

            meta[used] = new TrackVectorMeta(
                source.TrackId,
                source.ArtistId,
                source.ContentHash,
                songKey,
                source.CreatedAt,
                source.SkippedEarlyCount);

            used++;
        }

        if (skipped > 0)
        {
            logger.LogWarning(
                "Skipped {Skipped} embeddings whose vector does not hold {Dimension} floats", skipped, width);
        }

        if (used == 0)
            return EmbeddingSnapshot.Empty;

        if (used != capacity)
        {
            matrix = matrix.AsSpan(0, used * width).ToArray();
            meta = meta.AsSpan(0, used).ToArray();
        }

        return new EmbeddingSnapshot(matrix, meta, width);
    }
}
