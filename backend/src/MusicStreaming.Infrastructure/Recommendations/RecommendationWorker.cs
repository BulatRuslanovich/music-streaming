// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Application.Services.Recommendations;
using MusicStreaming.Domain.Entities.Recommendations;
using MusicStreaming.Infrastructure.Persistence;

namespace MusicStreaming.Infrastructure.Recommendations;

public class RecommendationWorker(
    IServiceScopeFactory scopeFactory,
    RecommendationRefreshQueue refreshQueue,
    IOptions<RecommendationOptions> options,
    TimeProvider clock,
    ILogger<RecommendationWorker> logger) : BackgroundService
{
    private RecommendationOptions Options => options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Options.Enabled)
        {
            logger.LogInformation("Recommendation processing is disabled by configuration");
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Options.StartupDelaySeconds), stoppingToken);
            await QueueEveryUserAsync(stoppingToken);

            var interval = TimeSpan.FromSeconds(Options.RegenerationDebounceSeconds);
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
        var startedAt = clock.GetUtcNow() - TimeSpan.FromSeconds(Options.RegenerationDebounceSeconds);

        foreach (var userId in userIds)
            refreshQueue.MarkDirty(userId, startedAt);
    }

    private async Task ProcessSettledUsersAsync(CancellationToken ct)
    {
        var debounce = TimeSpan.FromSeconds(Options.RegenerationDebounceSeconds);
        var settled = refreshQueue.ClaimSettled(clock.GetUtcNow(), debounce);

        foreach (var userId in settled)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await ProcessUserAsync(userId, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Refreshing recommendations for user {UserId} failed", userId);
            }
        }
    }

    private async Task ProcessUserAsync(Guid userId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var rollup = scope.ServiceProvider.GetRequiredService<ProfileRollupService>();
        var generation = scope.ServiceProvider.GetRequiredService<ShelfGenerationService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var metrics = scope.ServiceProvider.GetRequiredService<RecommendationMetrics>();

        await rollup.RollupAsync(userId, ct);

        if (!await ShelvesNeedRebuildAsync(db, userId, ct))
            return;

        var run = new RecommendationRun
        {
            UserId = userId,
            Trigger = RecommendationTrigger.Activity,
            StartedAt = clock.GetUtcNow(),
            Status = RecommendationRunStatus.Succeeded,
        };

        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            run.CandidateCount = await generation.GenerateAsync(userId, run.Id, ct);
            run.ShelfCount = await db.RecommendationCache.CountAsync(c => c.UserId == userId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.Status = RecommendationRunStatus.Failed;
            run.Error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            throw;
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            run.DurationMs = (int)elapsed.TotalMilliseconds;

            metrics.RecordGeneration(elapsed, run.CandidateCount);

            await RecordRunAsync(run);
        }
    }

    private async Task<bool> ShelvesNeedRebuildAsync(
        ApplicationDbContext db, Guid userId, CancellationToken ct)
    {
        var earliestExpiry = await db.RecommendationCache.AsNoTracking()
            .Where(c => c.UserId == userId)
            .MinAsync(c => (DateTimeOffset?)c.ExpiresAt, ct);

        return earliestExpiry is not { } expiresAt || expiresAt <= clock.GetUtcNow();
    }

    private async Task RecordRunAsync(RecommendationRun run)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            db.RecommendationRuns.Add(run);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not record recommendation run {RunId}", run.Id);
        }
    }
}
