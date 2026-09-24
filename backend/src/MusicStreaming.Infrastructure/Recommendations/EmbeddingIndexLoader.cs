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

    /// <summary>
    /// Собирает матрицу потоком, а не через промежуточный список.
    /// </summary>
    /// <remarks>
    /// Раньше строки материализовались целиком: на каждую приходился свой <c>float[]</c> от EF,
    /// поверх них список отобранных, и только потом плоская матрица. На пятидесяти тысячах треков
    /// это сотня мегабайт матрицы плюс столько же временных массивов — и всё это при ещё живом
    /// прошлом снимке, который до публикации никуда не девается. Здесь вектор копируется в своё
    /// место сразу, а массив от EF становится мусором на следующей итерации.
    /// </remarks>
    private async Task<EmbeddingSnapshot> BuildAsync(IApplicationDbContext db, CancellationToken ct)
    {
        var ready = db.TrackEmbeddings
            .AsNoTracking()
            .Where(embedding => embedding.Succeeded && embedding.Track != null);

        // Размерность задаёт та же строка, что и раньше — первая по TrackId. Всё, что не совпало,
        // отбрасывается: смешанная библиотека (сменили модель на полпути) не должна ломать индекс
        // целиком. Отсев по Dimension уходит в SQL, чтобы вместимость матрицы была известна точно.
        var dimension = await ready
            .OrderBy(embedding => embedding.TrackId)
            .Select(embedding => (int?)embedding.Dimension)
            .FirstOrDefaultAsync(ct);

        if (dimension is not { } width || width <= 0)
            return EmbeddingSnapshot.Empty;

        var matching = ready.Where(embedding => embedding.Dimension == width);
        var capacity = await matching.CountAsync(ct);

        if (capacity == 0)
            return EmbeddingSnapshot.Empty;

        var matrix = new float[capacity * width];
        var meta = new TrackVectorMeta[capacity];
        var used = 0;
        var skipped = 0;

        // AsNoTracking + проекция: вектор не должен попасть в отслеживаемую сущность, иначе
        // каждый SaveChanges в этой области начнёт сравнивать 512 float поэлементно.
        var stream = matching
            .OrderBy(embedding => embedding.TrackId)
            .Select(embedding => new
            {
                embedding.TrackId,
                embedding.Vector,
                embedding.ClusterId,
                embedding.Track!.ArtistId,
                embedding.Track.ContentHash,
                embedding.Track.Title,
                ArtistName = embedding.Track.Artist!.Name,
                embedding.Track.CreatedAt,
                ShownCount = embedding.Track.Stats == null ? 0 : embedding.Track.Stats.ShownCount,
                SkippedEarlyCount = embedding.Track.Stats == null ? 0 : embedding.Track.Stats.SkippedEarlyCount,
            })
            .AsAsyncEnumerable();

        await foreach (var source in stream.WithCancellation(ct))
        {
            // Объявленная размерность уже сошлась в SQL, а длина самого массива — нет: колонка и
            // массив могут разойтись, и тогда строка не годится.
            if (source.Vector.Length != width || used == capacity)
            {
                skipped++;
                continue;
            }

            var target = matrix.AsSpan(used * width, width);
            source.Vector.CopyTo(target);

            // Нормировка на всякий случай: дальше весь код считает скалярное произведение
            // косинусом, и одна ненормированная строка испортила бы сравнение молча.
            VectorMath.NormalizeInPlace(target);

            meta[used] = new TrackVectorMeta(
                source.TrackId,
                source.ArtistId,
                source.ContentHash,
                EmbeddingSnapshot.SongKeyOf(source.ArtistName, source.Title),
                source.CreatedAt,
                source.ClusterId ?? -1,
                source.ShownCount,
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

        // Между COUNT и выборкой строки могли добавиться или исчезнуть, а снимок требует матрицу
        // ровно под своё число строк. Обрезка — редкий путь и одно копирование.
        if (used != capacity)
        {
            matrix = matrix.AsSpan(0, used * width).ToArray();
            meta = meta.AsSpan(0, used).ToArray();
        }

        // Кластеризация внутри сборки: так ClusterId всегда согласован с той матрицей, которая
        // загружена, а не с той, что была в БД на момент прошлого обслуживания.
        var clustering = SphericalKMeans.Cluster(matrix, used, width, Options.Vector.ClusterCount);
        for (var row = 0; row < meta.Length; row++)
            meta[row] = meta[row] with { ClusterId = clustering.Labels[row] };

        return new EmbeddingSnapshot(matrix, meta, width, clock.GetUtcNow());
    }
}
