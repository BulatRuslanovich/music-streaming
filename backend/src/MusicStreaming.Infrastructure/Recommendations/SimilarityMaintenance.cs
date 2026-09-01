// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
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
    private const int AudioBucketCoreSize = 120;
    private const int TagCoreSize = 80;
    private const int MinimumSharedTags = 2;
    private const double MinimumPairingTagWeight = 0.3;

    /// <summary>Тег исполнителя описывает трек слабее, чем тег самого трека.</summary>
    private const double ArtistTagShare = 0.6;
    private const double TagWeight = 0.25;

    /// <summary>Ниже этой уверенности оценка тональности — шум, и совпадение ничего не значит.</summary>
    private const double MinimumKeyConfidence = 0.4;

    // Веса аудио-схожести. Темп, энергия, яркость, спад, динамика и громкость есть всегда;
    // тембр и тональность появляются только после анализа второй версии, поэтому их доли
    // вынесены отдельно, чтобы их можно было честно вернуть остальным.
    private const double TempoWeight = 0.28;
    private const double TimbreWeight = 0.24;
    private const double KeyWeight = 0.03;
    private const double AudioBaseWeight = TempoWeight + 0.16 + 0.10 + 0.06 + 0.08 + 0.05;
    private const double MinimumStoredScore = 0.05;

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
        Parameter("genre_core", NpgsqlDbType.Integer, GenreCoreSize),
        Parameter("audio_core", NpgsqlDbType.Integer, AudioBucketCoreSize),
        Parameter("tag_core", NpgsqlDbType.Integer, TagCoreSize),
        Parameter("min_shared_tags", NpgsqlDbType.Integer, MinimumSharedTags),
        Parameter("min_tag_weight", NpgsqlDbType.Double, MinimumPairingTagWeight),
        Parameter("artist_tag_share", NpgsqlDbType.Double, ArtistTagShare),
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
        Parameter("shrinkage", NpgsqlDbType.Double, Options.CollaborativeShrinkage),
        Parameter("pivot", NpgsqlDbType.Double, Options.CollaborativeBlendPivot),
        Parameter("w_tempo", NpgsqlDbType.Double, TempoWeight),
        Parameter("w_timbre", NpgsqlDbType.Double, TimbreWeight),
        Parameter("w_key", NpgsqlDbType.Double, KeyWeight),
        Parameter("w_audio_base", NpgsqlDbType.Double, AudioBaseWeight),
        Parameter("key_confidence", NpgsqlDbType.Double, MinimumKeyConfidence),
        Parameter("min_score", NpgsqlDbType.Double, MinimumStoredScore),
        Parameter("top_k", NpgsqlDbType.Integer, Options.SimilarTopK),
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
        var eventCutoff = now.AddDays(-Options.EventRetentionDays);
        var impressionCutoff = now.AddDays(-Options.ImpressionRetentionDays);

        var events = await db.PlaybackEvents.Where(e => e.OccurredAt < eventCutoff).ExecuteDeleteAsync(ct);
        var impressions = await db.RecommendationImpressions
            .Where(i => i.ShownAt < impressionCutoff)
            .ExecuteDeleteAsync(ct);

        var runs = await db.RecommendationRuns
            .Where(r => r.StartedAt < impressionCutoff)
            .ExecuteDeleteAsync(ct);

        if (events + impressions + runs > 0)
        {
            logger.LogInformation(
                "Pruned {Events} events, {Impressions} impressions and {Runs} run records",
                events, impressions, runs);
        }

        await PruneOrphanTagsAsync(ct);
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
