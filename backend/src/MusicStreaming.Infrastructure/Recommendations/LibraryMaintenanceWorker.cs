// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations;

namespace MusicStreaming.Infrastructure.Recommendations;

public class LibraryMaintenanceWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<RecommendationOptions> options,
    ILogger<LibraryMaintenanceWorker> logger) : ScheduledWorker(scopeFactory, logger)
{
    private RecommendationOptions Options => options.Value;

    // Вдвое дольше остальных: обслуживание тяжелее прочих проходов, и стартовать вместе с ними
    // ему незачем.
    protected override TimeSpan StartupDelay => TimeSpan.FromSeconds(RecommendationTuning.Maintenance.StartupDelaySeconds * 2);
    protected override TimeSpan? Interval => TimeSpan.FromHours(RecommendationTuning.Maintenance.SimilarityIntervalHours);
    protected override string Name => "Library maintenance";

    protected override bool ShouldRun() => Options.Enabled;

    protected override async Task RunPassAsync(CancellationToken ct)
    {
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
            logger.LogError(ex, "Library maintenance pass failed");
        }
    }
}
