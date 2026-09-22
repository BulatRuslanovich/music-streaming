// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations.Embeddings;

namespace MusicStreaming.Infrastructure.Recommendations;

/// <summary>
/// Перечитывает матрицу эмбеддингов и публикует новый снимок. Полная пересборка на 50k занимает
/// секунды, поэтому проходу предшествует дешёвая проба: если число строк и время последнего
/// анализа не изменились, читать нечего.
/// </summary>
public class EmbeddingIndexLoader(
    IServiceScopeFactory scopeFactory,
    EmbeddingIndex index,
    IOptions<RecommendationOptions> options,
    TimeProvider clock,
    ILogger<EmbeddingIndexLoader> logger) : ScheduledWorker(scopeFactory, logger)
{
    private RecommendationOptions Options => options.Value;

    private int _lastCount = -1;
    private DateTimeOffset? _lastAnalyzedAt;

    protected override TimeSpan StartupDelay => TimeSpan.FromSeconds(Options.Maintenance.StartupDelaySeconds);
    protected override TimeSpan? Interval => TimeSpan.FromMinutes(Options.Vector.IndexReloadMinutes);
    protected override string Name => "Embedding index loader";

    protected override bool ShouldRun() => Options.Enabled;

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
        // AsNoTracking + проекция: вектор не должен попасть в отслеживаемую сущность, иначе
        // каждый SaveChanges в этой области начнёт сравнивать 512 float поэлементно.
        var rows = await db.TrackEmbeddings
            .AsNoTracking()
            .Where(embedding => embedding.Succeeded && embedding.Track != null)
            .OrderBy(embedding => embedding.TrackId)
            .Select(embedding => new
            {
                embedding.TrackId,
                embedding.Vector,
                embedding.Dimension,
                embedding.ClusterId,
                embedding.Track!.ArtistId,
                embedding.Track.ContentHash,
                embedding.Track.Title,
                ArtistName = embedding.Track.Artist!.Name,
                embedding.Track.CreatedAt,
                ShownCount = embedding.Track.Stats == null ? 0 : embedding.Track.Stats.ShownCount,
                SkippedEarlyCount = embedding.Track.Stats == null ? 0 : embedding.Track.Stats.SkippedEarlyCount,
            })
            .ToListAsync(ct);

        if (rows.Count == 0)
            return EmbeddingSnapshot.Empty;

        // Размерность задаёт первая строка; всё, что не совпало, отбрасывается. Смешанная
        // библиотека (сменили модель на полпути) не должна ломать индекс целиком.
        var dimension = rows[0].Dimension;
        var usable = rows.Where(row => row.Dimension == dimension && row.Vector.Length == dimension).ToList();

        if (usable.Count != rows.Count)
        {
            logger.LogWarning(
                "Skipped {Skipped} embeddings whose dimension differs from {Dimension}",
                rows.Count - usable.Count,
                dimension);
        }

        if (usable.Count == 0)
            return EmbeddingSnapshot.Empty;

        var matrix = new float[usable.Count * dimension];
        var meta = new TrackVectorMeta[usable.Count];

        for (var row = 0; row < usable.Count; row++)
        {
            var source = usable[row];
            var target = matrix.AsSpan(row * dimension, dimension);
            source.Vector.CopyTo(target);

            // Нормировка на всякий случай: дальше весь код считает скалярное произведение
            // косинусом, и одна ненормированная строка испортила бы сравнение молча.
            VectorMath.NormalizeInPlace(target);

            meta[row] = new TrackVectorMeta(
                source.TrackId,
                source.ArtistId,
                source.ContentHash,
                EmbeddingSnapshot.SongKeyOf(source.ArtistName, source.Title),
                source.CreatedAt,
                source.ClusterId ?? -1,
                source.ShownCount,
                source.SkippedEarlyCount);
        }

        // Кластеризация внутри сборки: так ClusterId всегда согласован с той матрицей, которая
        // загружена, а не с той, что была в БД на момент прошлого обслуживания.
        var clustering = SphericalKMeans.Cluster(matrix, usable.Count, dimension, Options.Vector.ClusterCount);
        for (var row = 0; row < meta.Length; row++)
            meta[row] = meta[row] with { ClusterId = clustering.Labels[row] };

        return new EmbeddingSnapshot(matrix, meta, dimension, clock.GetUtcNow());
    }
}
