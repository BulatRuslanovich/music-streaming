// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Options;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Infrastructure.Recommendations;

public class LibraryMaintenanceWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<RecommendationOptions> options,
    TimeProvider clock,
    ILogger<LibraryMaintenanceWorker> logger) : BackgroundService
{
    private RecommendationOptions Options => options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Options.Enabled)
            return;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Options.StartupDelaySeconds * 2), stoppingToken);

            using var timer = new PeriodicTimer(TimeSpan.FromHours(Options.SimilarityIntervalHours));

            do
            {
                await RunPassAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Library maintenance stopped unexpectedly");
        }
    }

    private async Task RunPassAsync(CancellationToken ct)
    {
        var run = new RecommendationRun
        {
            Trigger = RecommendationTrigger.Scheduled,
            StartedAt = clock.GetUtcNow(),
            Status = RecommendationRunStatus.Succeeded,
        };

        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();

        try
        {
            using var scope = scopeFactory.CreateScope();
            var maintenance = scope.ServiceProvider.GetRequiredService<SimilarityMaintenance>();

            await maintenance.PruneAsync(ct);
            await maintenance.RefreshTrackStatsAsync(ct);
            await maintenance.RefreshSimilarityAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RecommendationRunPersistence.MarkFailed(run, ex);
            logger.LogError(ex, "Library maintenance pass failed");
        }

        run.DurationMs = (int)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

        await RecommendationRunPersistence.TrySaveAsync(
            scopeFactory,
            run,
            logger,
            "Could not record the library maintenance run {RunId}");
    }
}
