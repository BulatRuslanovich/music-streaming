// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Application.Services.Recommendations;
using MusicStreaming.Domain.Entities.Recommendations;
using MusicStreaming.Infrastructure.Persistence;

namespace MusicStreaming.Infrastructure.Recommendations;

/// <summary>
/// Пишет показы полок, снятые с горячего пути отдачи главной страницы.
/// </summary>
public class ImpressionWorker(
    IServiceScopeFactory scopeFactory,
    ImpressionQueue queue,
    RecommendationMetrics metrics,
    ILogger<ImpressionWorker> logger) : BackgroundService
{
    private const int MaxBatchSize = 64;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            List<ImpressionBatch> batches;

            try
            {
                batches = await queue.ReadBatchAsync(MaxBatchSize, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            if (batches.Count == 0)
                continue;

            try
            {
                // Несколько открытий главной одним пользователем подряд схлопываются в один
                // проход: дедупликация всё равно идёт по (пользователь, трек, полка) за сутки.
                foreach (var perUser in batches.GroupBy(batch => batch.UserId))
                    await WriteSafelyAsync(perUser.Key, perUser.ToList(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            finally
            {
                queue.MarkHandled(batches.Count);
            }
        }
    }

    /// <summary>
    /// Показы одного человека не должны уносить чужие: партия пишется своим SaveChanges, и её
    /// падение здесь и остаётся.
    /// </summary>
    private async Task WriteSafelyAsync(Guid userId, List<ImpressionBatch> batches, CancellationToken ct)
    {
        try
        {
            await WriteAsync(userId, batches, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Writing shelf impressions for user {UserId} failed", userId);
        }
    }

    private async Task WriteAsync(Guid userId, List<ImpressionBatch> batches, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var shownAt = batches.Max(batch => batch.ShownAt);
        var since = new DateTimeOffset(shownAt.UtcDateTime.Date, TimeSpan.Zero);

        // При схлопывании выигрывает первое вхождение: позиция в самой ранней отдаче честнее.
        var shown = new Dictionary<(string ShelfKey, Guid TrackId), int>();
        foreach (var item in batches.SelectMany(batch => batch.Items))
            shown.TryAdd((item.ShelfKey, item.TrackId), item.Position);

        if (shown.Count == 0)
            return;

        var trackIds = shown.Keys.Select(key => key.TrackId).Distinct().ToList();

        var alreadyShown = (await db.RecommendationImpressions.AsNoTracking()
                .Where(i => i.UserId == userId && i.ShownAt >= since && trackIds.Contains(i.TrackId))
                .Select(i => new { i.TrackId, i.ShelfKey })
                .ToListAsync(ct))
            .Select(i => (i.ShelfKey, i.TrackId))
            .ToHashSet();

        // Трек могли удалить между отдачей полки и этой записью. Вставка целой партии упала бы
        // на внешнем ключе — вместе с показами, к удалённому треку отношения не имеющими.
        var live = (await db.Tracks.AsNoTracking()
                .Where(track => trackIds.Contains(track.Id))
                .Select(track => track.Id)
                .ToListAsync(ct))
            .ToHashSet();

        var fresh = shown
            .Where(entry => !alreadyShown.Contains(entry.Key) && live.Contains(entry.Key.TrackId))
            .Select(entry => new RecommendationImpression
            {
                UserId = userId,
                TrackId = entry.Key.TrackId,
                ShelfKey = entry.Key.ShelfKey,
                Position = entry.Value,
                ShownAt = shownAt,
            })
            .ToList();

        if (fresh.Count == 0)
            return;

        db.RecommendationImpressions.AddRange(fresh);
        await db.SaveChangesAsync(ct);

        foreach (var group in fresh.GroupBy(impression => ShelfKeys.BaseOf(impression.ShelfKey)))
            metrics.RecordImpressions(group.Count(), group.Key);
    }
}
