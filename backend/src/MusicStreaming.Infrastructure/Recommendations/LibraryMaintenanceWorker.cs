using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Options;
using MusicStreaming.Domain.Entities.Recommendations;
using MusicStreaming.Infrastructure.Persistence;

namespace MusicStreaming.Infrastructure.Recommendations;

/// <summary>
/// Rebuilds the library-wide models on a slow schedule: popularity, track similarity, and the
/// retention sweep over raw events.
///
/// <para>
/// Separate from the per-user worker because the cost profile is different — this is one heavy
/// pass over the whole library every few hours, not a light pass per active listener.
/// </para>
/// </summary>
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
            // Long enough after start that the first pass does not compete with migrations, the
            // cover backfill and whatever the user is doing in their first seconds on the page.
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
        using var scope = scopeFactory.CreateScope();
        var maintenance = scope.ServiceProvider.GetRequiredService<SimilarityMaintenance>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var run = new RecommendationRun
        {
            Trigger = RecommendationTrigger.Scheduled,
            StartedAt = clock.GetUtcNow(),
            Status = RecommendationRunStatus.Succeeded,
        };

        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();

        try
        {
            await maintenance.PruneAsync(ct);
            await maintenance.RefreshTrackStatsAsync(ct);
            await maintenance.RefreshSimilarityAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.Status = RecommendationRunStatus.Failed;
            run.Error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            logger.LogError(ex, "Library maintenance pass failed");
        }

        run.DurationMs = (int)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

        db.RecommendationRuns.Add(run);
        await db.SaveChangesAsync(ct);
    }
}
