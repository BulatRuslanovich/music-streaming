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

/// <summary>
/// Keeps taste profiles and shelves in step with what people are listening to.
///
/// <para>
/// Rolling up and generating run back to back for the same user, in that order, so a shelf is
/// never built from a profile that is one batch out of date.
/// </para>
/// </summary>
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

    /// <summary>
    /// Sweeps every account once after start. A pass that was interrupted mid-batch left a
    /// watermark behind its events, and this is what picks that back up; when there is nothing new
    /// the rollup is a single indexed query per user and costs nothing.
    /// </summary>
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

    /// <summary>
    /// Rolls up, then generates — in that order and in one scope, so a shelf is never built from a
    /// profile that is a batch out of date.
    /// </summary>
    private async Task ProcessUserAsync(Guid userId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var rollup = scope.ServiceProvider.GetRequiredService<ProfileRollupService>();
        var generation = scope.ServiceProvider.GetRequiredService<ShelfGenerationService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var metrics = scope.ServiceProvider.GetRequiredService<RecommendationMetrics>();

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
            await rollup.RollupAsync(userId, ct);
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

            db.RecommendationRuns.Add(run);
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }
}
