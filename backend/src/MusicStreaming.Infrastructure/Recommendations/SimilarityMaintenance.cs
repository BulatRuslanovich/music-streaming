// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Domain.Entities.Recommendations;
using MusicStreaming.Infrastructure.Persistence;
using MusicStreaming.Infrastructure.Recommendations.Sql;
using Npgsql;
using NpgsqlTypes;

namespace MusicStreaming.Infrastructure.Recommendations;

/// <summary>Что сделал проход пересчёта: пригодилось тестам и логам, чтобы режим был виден.</summary>
public record SimilarityRefresh(bool Ran, bool WholeLibrary, int Tracks, int Rows)
{
    public static readonly SimilarityRefresh Unchanged = new(false, false, 0, 0);
}

public class SimilarityMaintenance(
    ApplicationDbContext db,
    IMusicStorage storage,
    IImageStorage images,
    IOptions<RecommendationOptions> options,
    TimeProvider clock,
    ILogger<SimilarityMaintenance> logger)
{
    private const int CoOccurrenceWindowSeconds = 1800;
    private const int MaxCuratedPlaylistSize = 100;
    private const int ArtistCoreSize = 200;
    private const int GenreCoreSize = 60;

    /// <summary>Сколько треков альбома участвует в парах.</summary>
    /// <remarks>
    /// Порог выбран заведомо выше любого настоящего альбома: он не отсекает музыку, а страхует от
    /// одного альбома-свалки, в который плохо размеченный импорт складывает тысячи треков.
    /// </remarks>
    private const int AlbumCoreSize = 100;
    /// <summary>Сколько представителей берётся от каждого кластера эмбеддингов на пары.</summary>
    private const int ClusterCoreSize = 120;
    private const int TagCoreSize = 80;
    private const int MinimumSharedTags = 2;
    private const double MinimumPairingTagWeight = 0.3;

    private const double TagWeight = 0.25;

    private const double MinimumStoredScore = 0.05;

    /// <summary>Ниже этого веса ребро перехода — шум, и место в таблице оно занимает напрасно.</summary>
    private const double MinimumTransitionWeight = 0.01;

    /// <summary>За этой долей изменившихся треков область охватывает почти всё, и полная дешевле.</summary>
    private const double FullRebuildShare = 0.25;

    /// <summary>Как часто библиотека пересобирается целиком, даже если ничего не менялось.</summary>
    private const int FullRebuildIntervalHours = 24;
    private RecommendationOptions Options => options.Value;

    public async Task RefreshTrackStatsAsync(CancellationToken ct = default)
    {
        var affected = await db.Database.ExecuteSqlRawAsync(SimilaritySql.RefreshTrackStats, ct);
        logger.LogDebug("Refreshed statistics for {Count} tracks", affected);
    }

    /// <summary>
    /// Пересчёт схожести. Что именно изменилось, определяется отпечатком входов каждого трека:
    /// не изменилось ничего — проход не делает вообще ничего; изменилось немного — пересобирается
    /// только затронутая область; изменилось много или подошёл срок — вся библиотека.
    /// </summary>
    public async Task<SimilarityRefresh> RefreshSimilarityAsync(CancellationToken ct = default)
    {
        var dirty = await DirtyTracksAsync(ct);
        var total = await db.Tracks.CountAsync(ct);
        var fullRebuildDue = await FullRebuildDueAsync(ct);

        if (dirty.Count == 0 && !fullRebuildDue)
        {
            logger.LogDebug("Track similarity is up to date: nothing changed since the last pass");
            return SimilarityRefresh.Unchanged;
        }

        // Область в долях библиотеки растёт быстрее числа изменившихся треков — у каждого из них
        // десятки соседей, — поэтому за порогом полная пересборка просто дешевле.
        var whole = fullRebuildDue
                    || total == 0
                    || dirty.Count >= total * FullRebuildShare;

        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();

        // Пересборка читает таблицы, которые сильно меняются между проходами: после крупного
        // импорта планировщик работает по устаревшей статистике и выбирает вложенные циклы там,
        // где нужен хеш-джойн, — запрос из секунд превращается в минуты. ANALYZE стоит доли секунды,
        // но проходу, которому нечего делать, не нужен и он.
        await db.Database.ExecuteSqlRawAsync(SimilaritySql.AnalyzeInputs, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await db.Database.ExecuteSqlRawAsync(SimilaritySql.BuildPairs, PairParameters(), ct);

        var scope = whole ? [] : await ScopeAsync(dirty, ct);

        if (!whole && scope.Count == 0)
        {
            await transaction.RollbackAsync(ct);
            logger.LogDebug("Track similarity is up to date: the changed tracks pair with nothing");
            return SimilarityRefresh.Unchanged;
        }

        await DeleteStaleAsync(whole, scope, ct);

        var written = await db.Database.ExecuteSqlRawAsync(SimilaritySql.Score, ScoreParameters(whole, scope), ct);

        await db.Database.ExecuteSqlRawAsync(
            SimilaritySql.RewriteState, [WholeLibrary(whole), Scope(whole ? dirty : scope)], ct);

        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "{Mode} track similarity in {Elapsed:0.0} s: {Count} neighbour rows over {Scope} of {Total} tracks",
            whole ? "Rebuilt" : "Refreshed",
            System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalSeconds,
            written,
            whole ? total : scope.Count,
            total);

        return new SimilarityRefresh(true, whole, whole ? total : scope.Count, written);
    }

    private async Task<List<Guid>> DirtyTracksAsync(CancellationToken ct) =>
        await db.Database.SqlQueryRaw<Guid>(SimilaritySql.DirtyTracks).ToListAsync(ct);

    private async Task<bool> FullRebuildDueAsync(CancellationToken ct)
    {
        // Самый старый отпечаток — это момент последней полной пересборки: только она переписывает
        // их все. Периодически полная нужна, потому что популярность в отпечаток не входит, а от неё
        // зависит, какие треки представляют жанр или тег в парах.
        var oldest = await db.TrackSimilarityStates
            .OrderBy(state => state.ComputedAt)
            .Select(state => (DateTimeOffset?)state.ComputedAt)
            .FirstOrDefaultAsync(ct);

        return oldest is not { } moment
               || clock.GetUtcNow() - moment >= TimeSpan.FromHours(FullRebuildIntervalHours);
    }

    private async Task<List<Guid>> ScopeAsync(List<Guid> dirty, CancellationToken ct) =>
        await db.Database.SqlQueryRaw<Guid>(SimilaritySql.Scope, Dirty(dirty)).ToListAsync(ct);

    private async Task DeleteStaleAsync(bool whole, List<Guid> scope, CancellationToken ct)
    {
        if (whole)
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM track_similarity", ct);
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM track_similarity WHERE track_id = ANY(@scope)", [Scope(scope)], ct);
    }

    private NpgsqlParameter[] PairParameters() =>
    [
        Parameter("artist_core", NpgsqlDbType.Integer, ArtistCoreSize),
        Parameter("album_core", NpgsqlDbType.Integer, AlbumCoreSize),
        Parameter("genre_core", NpgsqlDbType.Integer, GenreCoreSize),
        Parameter("audio_core", NpgsqlDbType.Integer, ClusterCoreSize),
        Parameter("tag_core", NpgsqlDbType.Integer, TagCoreSize),
        Parameter("min_shared_tags", NpgsqlDbType.Integer, MinimumSharedTags),
        Parameter("min_tag_weight", NpgsqlDbType.Double, MinimumPairingTagWeight),
        Parameter("artist_tag_share", NpgsqlDbType.Double, TagWeights.ArtistShare),
        Parameter("window", NpgsqlDbType.Integer, CoOccurrenceWindowSeconds),
        Parameter("max_playlist", NpgsqlDbType.Integer, MaxCuratedPlaylistSize),
    ];

    private NpgsqlParameter[] ScoreParameters(bool whole, List<Guid> scope) =>
    [
        // Вместе с @w_tag суммируются в единицу: тег-вектор частично замещает жанровый ярлык.
        Parameter("w_artist", NpgsqlDbType.Double, 0.35),
        Parameter("w_album", NpgsqlDbType.Double, 0.16),
        Parameter("w_genre", NpgsqlDbType.Double, 0.12),
        Parameter("w_year", NpgsqlDbType.Double, 0.08),
        Parameter("w_duration", NpgsqlDbType.Double, 0.04),
        Parameter("w_tag", NpgsqlDbType.Double, TagWeight),
        Parameter("shrinkage", NpgsqlDbType.Double, Options.Collaborative.Shrinkage),
        Parameter("pivot", NpgsqlDbType.Double, Options.Collaborative.BlendPivot),
        Parameter("min_score", NpgsqlDbType.Double, MinimumStoredScore),
        Parameter("top_k", NpgsqlDbType.Integer, Options.Shelves.SimilarTopK),
        WholeLibrary(whole),
        Scope(scope),
    ];

    private static NpgsqlParameter Dirty(List<Guid> dirty) =>
        new("dirty", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = dirty.ToArray() };

    private static NpgsqlParameter Scope(List<Guid> scope) =>
        new("scope", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = scope.ToArray() };

    private static NpgsqlParameter WholeLibrary(bool whole) =>
        new("whole_library", NpgsqlDbType.Boolean) { Value = whole };

    public async Task PruneAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var eventCutoff = now.AddDays(-Options.Maintenance.EventRetentionDays);
        var impressionCutoff = now.AddDays(-Options.Maintenance.ImpressionRetentionDays);

        var statCutoff = now.AddDays(-Options.Maintenance.ListeningStatRetentionDays);

        var events = await db.PlaybackEvents.Where(e => e.OccurredAt < eventCutoff).ExecuteDeleteAsync(ct);
        var impressions = await db.RecommendationImpressions
            .Where(i => i.ShownAt < impressionCutoff)
            .ExecuteDeleteAsync(ct);

        var runs = await db.RecommendationRuns
            .Where(r => r.StartedAt < impressionCutoff)
            .ExecuteDeleteAsync(ct);

        var stats = await db.ListeningStats.Where(s => s.Hour < statCutoff).ExecuteDeleteAsync(ct);

        // У таблицы есть и expires_at, и индекс по нему, но удалять по ним было нечему: истёкшие
        // подавления только отфильтровывались на чтении и лежали вечно.
        var suppressions = await db.RecommendationSuppressions
            .Where(s => s.ExpiresAt != null && s.ExpiresAt < now)
            .ExecuteDeleteAsync(ct);

        if (events + impressions + runs + stats + suppressions > 0)
        {
            logger.LogInformation(
                "Pruned {Events} events, {Impressions} impressions, {Runs} run records, "
                + "{Stats} hourly rollups and {Suppressions} expired suppressions",
                events, impressions, runs, stats, suppressions);
        }

        await DecayTransitionsAsync(now, ct);
        await PruneOrphanTagsAsync(ct);
    }

    /// <summary>
    /// Затухание графа переходов.
    /// </summary>
    /// <remarks>
    /// Вес пары считался только вверх, поэтому соседство, наигранное два года назад, навсегда
    /// перевешивало свежее поведение — в отличие от аффинити, у которых затухание было с начала.
    /// Расчёт идёт от <c>updated_at</c>, а не от числа проходов: пара, которую продолжают играть,
    /// теряет мало, заброшенная — много, и результат не зависит от того, как часто идёт проход.
    /// Обнулившиеся рёбра удаляются — иначе таблица копила бы шум с нулевым весом.
    /// </remarks>
    private async Task DecayTransitionsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var halfLifeSeconds = Options.Decay.TransitionHalfLifeDays * 86400;

        var decayed = await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE track_transitions
            SET weight = weight * pow(0.5, EXTRACT(EPOCH FROM ({now} - updated_at)) / {halfLifeSeconds}),
                updated_at = {now}
            WHERE updated_at < {now}
            """, ct);

        var dropped = await db.TrackTransitions
            .Where(transition => transition.Weight < MinimumTransitionWeight)
            .ExecuteDeleteAsync(ct);

        if (decayed + dropped > 0)
            logger.LogDebug("Decayed {Decayed} transitions and dropped {Dropped} spent edges", decayed, dropped);
    }

    private async Task PruneOrphanTagsAsync(CancellationToken ct = default)
    {
        var coverPaths = await db.Albums
            .Where(a => !a.Tracks.Any() && a.CoverPath != null)
            .Select(a => a.CoverPath!)
            .ToListAsync(ct);

        var albums = await db.Albums.Where(a => !a.Tracks.Any()).ExecuteDeleteAsync(ct);

        var imagePaths = await db.Artists
            .Where(a => !a.Tracks.Any() && !a.Albums.Any() && !a.TrackCredits.Any() && a.ImagePath != null)
            .Select(a => a.ImagePath!)
            .ToListAsync(ct);

        var artists = await db.Artists
            .Where(a => !a.Tracks.Any() && !a.Albums.Any() && !a.TrackCredits.Any())
            .ExecuteDeleteAsync(ct);

        var genres = await db.Genres.Where(g => !g.Tracks.Any()).ExecuteDeleteAsync(ct);

        foreach (var path in coverPaths)
            images.DeleteCover(path);

        foreach (var path in imagePaths)
            storage.Delete(path);

        if (albums + artists + genres > 0)
        {
            logger.LogInformation(
                "Pruned {Albums} orphaned albums, {Artists} artists and {Genres} genres",
                albums, artists, genres);
        }
    }

    private static NpgsqlParameter Parameter(string name, NpgsqlDbType type, object value) =>
        new(name, type) { Value = value };
}
