// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using App.Recommendations;

namespace Infrastructure.Recommendations;

public class LibraryMaintenanceWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<LibraryMaintenanceWorker> logger) : ScheduledWorker(scopeFactory, logger)
{
    protected override TimeSpan StartupDelay => TimeSpan.FromSeconds(RecommendationTuning.Maintenance.StartupDelaySeconds * 2);
    protected override TimeSpan? Interval => TimeSpan.FromHours(RecommendationTuning.Maintenance.IntervalHours);
    protected override string Name => "Library maintenance";

    protected override async Task RunPassAsync(CancellationToken ct)
    {
        try
        {
            using var scope = CreateScope();
            var maintenance = scope.ServiceProvider.GetRequiredService<LibraryMaintenance>();

            await maintenance.PruneAsync(ct);
            await maintenance.RefreshTrackStatsAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Library maintenance pass failed");
        }
    }
}
