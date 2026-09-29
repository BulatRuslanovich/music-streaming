// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Infrastructure.Persistence;
using MusicStreaming.Application.Recommendations;

namespace MusicStreaming.Infrastructure.Recommendations;

/// <summary>
/// The periodic pass over the library: track statistics, retention, the decay of the transition
/// graph and orphaned albums, artists and genres.
/// </summary>
public class LibraryMaintenance(
    ApplicationDbContext db,
    IMusicStorage storage,
    IImageStorage images,
    TimeProvider clock,
    ILogger<LibraryMaintenance> logger)
{
    /// <summary>Ниже этого веса ребро перехода — шум, и место в таблице оно занимает напрасно.</summary>
    private const double MinimumTransitionWeight = 0.01;

    /// <summary>
    /// Запрос лежит рядом как <c>.sql</c>, а не строкой в C#: он длиннее самого метода, и в
    /// отдельном файле его видно редактору — с подсветкой и форматированием.
    /// </summary>
    private static readonly Lazy<string> RefreshTrackStatsSql = new(() =>
    {
        const string Resource = "MusicStreaming.Infrastructure.Recommendations.Sql.refresh-track-stats.sql";

        using var stream = typeof(LibraryMaintenance).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"Embedded SQL resource '{Resource}' is missing.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    });

    public async Task RefreshTrackStatsAsync(CancellationToken ct = default)
    {
        var affected = await db.Database.ExecuteSqlRawAsync(RefreshTrackStatsSql.Value, ct);
        logger.LogDebug("Refreshed statistics for {Count} tracks", affected);
    }

    public async Task PruneAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var eventCutoff = now.AddDays(-RecommendationTuning.Maintenance.EventRetentionDays);

        var statCutoff = now.AddDays(-RecommendationTuning.Maintenance.ListeningStatRetentionDays);

        var events = await db.PlaybackEvents.Where(e => e.OccurredAt < eventCutoff).ExecuteDeleteAsync(ct);

        var stats = await db.ListeningStats.Where(s => s.Hour < statCutoff).ExecuteDeleteAsync(ct);

        if (events + stats > 0)
            logger.LogInformation("Pruned {Events} events and {Stats} hourly rollups", events, stats);

        await DecayTransitionsAsync(now, ct);
        await PruneOrphansAsync(ct);
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
        var halfLifeSeconds = RecommendationTuning.Decay.TransitionHalfLifeDays * 86400;

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

    private async Task PruneOrphansAsync(CancellationToken ct = default)
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
}
