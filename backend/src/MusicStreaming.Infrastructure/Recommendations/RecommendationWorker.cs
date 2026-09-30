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
        var debounce = TimeSpan.FromSeconds(RecommendationTuning.Maintenance.RegenerationDebounceSeconds);
        var maxDelay = TimeSpan.FromSeconds(RecommendationTuning.Maintenance.RegenerationMaxDelaySeconds);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(RecommendationTuning.Maintenance.StartupDelaySeconds), stoppingToken);

            using (var scope = scopeFactory.CreateScope())
            {
                var userIds = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users
                    .AsNoTracking().Select(u => u.Id).ToListAsync(stoppingToken);

                var startedAt = clock.GetUtcNow() - debounce;
                foreach (var userId in userIds)
                    refreshQueue.MarkDirty(userId, startedAt);
            }

            using var timer = new PeriodicTimer(debounce);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                foreach (var refresh in refreshQueue.ClaimSettled(clock.GetUtcNow(), debounce, maxDelay))
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    try
                    {
                        using var scope = scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                        await scope.ServiceProvider.GetRequiredService<ProfileRollupService>()
                            .RollupAsync(refresh.UserId, stoppingToken);

                        if (refresh.ForceRebuild
                            || await db.RecommendationCache.AsNoTracking()
                                .Where(c => c.UserId == refresh.UserId)
                                .MinAsync(c => (DateTimeOffset?)c.ExpiresAt, stoppingToken) is not { } expiresAt
                            || expiresAt <= clock.GetUtcNow())
                        {
                            await scope.ServiceProvider.GetRequiredService<ShelfGenerationService>()
                                .GenerateAsync(refresh.UserId, stoppingToken);
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Refreshing recommendations for user {UserId} failed", refresh.UserId);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Recommendation processing stopped unexpectedly");
        }
    }
}
