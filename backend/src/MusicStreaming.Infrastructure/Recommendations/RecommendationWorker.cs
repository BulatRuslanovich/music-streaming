// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Application.Services.Recommendations;
using MusicStreaming.Infrastructure.Persistence;

namespace MusicStreaming.Infrastructure.Recommendations;

public class RecommendationWorker(
    IServiceScopeFactory scopeFactory,
    RecommendationRefreshQueue refreshQueue,
    TimeProvider clock,
    ILogger<RecommendationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(RecommendationTuning.Maintenance.StartupDelaySeconds), stoppingToken);
            await QueueEveryUserAsync(stoppingToken);

            var interval = TimeSpan.FromSeconds(RecommendationTuning.Maintenance.RegenerationDebounceSeconds);
            using var timer = new PeriodicTimer(interval);

            while (await timer.WaitForNextTickAsync(stoppingToken))
                await ProcessSettledUsersAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Recommendation processing stopped unexpectedly");
        }
    }

    private async Task QueueEveryUserAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var userIds = await db.Users.AsNoTracking().Select(u => u.Id).ToListAsync(ct);
        var startedAt = clock.GetUtcNow() - TimeSpan.FromSeconds(RecommendationTuning.Maintenance.RegenerationDebounceSeconds);

        foreach (var userId in userIds)
            refreshQueue.MarkDirty(userId, startedAt);
    }

    private async Task ProcessSettledUsersAsync(CancellationToken ct)
    {
        var debounce = TimeSpan.FromSeconds(RecommendationTuning.Maintenance.RegenerationDebounceSeconds);
        var maxDelay = TimeSpan.FromSeconds(RecommendationTuning.Maintenance.RegenerationMaxDelaySeconds);
        var settled = refreshQueue.ClaimSettled(clock.GetUtcNow(), debounce, maxDelay);

        foreach (var refresh in settled)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await ProcessUserAsync(refresh, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex, "Refreshing recommendations for user {UserId} failed", refresh.UserId);
            }
        }
    }

    private async Task ProcessUserAsync(RecommendationRefreshRequest refresh, CancellationToken ct)
    {
        var userId = refresh.UserId;
        using var scope = scopeFactory.CreateScope();
        var rollup = scope.ServiceProvider.GetRequiredService<ProfileRollupService>();
        var generation = scope.ServiceProvider.GetRequiredService<ShelfGenerationService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await rollup.RollupAsync(userId, ct);

        if (!refresh.ForceRebuild && !await ShelvesNeedRebuildAsync(db, userId, ct))
            return;

        await generation.GenerateAsync(userId, ct);
    }

    private async Task<bool> ShelvesNeedRebuildAsync(
        ApplicationDbContext db, Guid userId, CancellationToken ct)
    {
        var earliestExpiry = await db.RecommendationCache.AsNoTracking()
            .Where(c => c.UserId == userId)
            .MinAsync(c => (DateTimeOffset?)c.ExpiresAt, ct);

        return earliestExpiry is not { } expiresAt || expiresAt <= clock.GetUtcNow();
    }

}
