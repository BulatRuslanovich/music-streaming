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
    ILogger<LibraryMaintenanceWorker> logger) : ScheduledWorker(scopeFactory, logger)
{
    private RecommendationOptions Options => options.Value;

    // Вдвое дольше остальных: обслуживание тяжелее прочих проходов, и стартовать вместе с ними
    // ему незачем.
    protected override TimeSpan StartupDelay => TimeSpan.FromSeconds(Options.StartupDelaySeconds * 2);
    protected override TimeSpan? Interval => TimeSpan.FromHours(Options.SimilarityIntervalHours);
    protected override string Name => "Library maintenance";

    protected override bool ShouldRun() => Options.Enabled;

    protected override async Task RunPassAsync(CancellationToken ct)
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
            using var scope = CreateScope();
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
            Scopes,
            run,
            logger,
            "Could not record the library maintenance run {RunId}");
    }
}
