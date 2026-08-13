using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Options;
using MusicStreaming.Domain.Entities.Recommendations;
using MusicStreaming.Infrastructure.Persistence;

namespace MusicStreaming.Infrastructure.Recommendations;

/// <summary>
/// Перестраивает модели масштаба всей библиотеки по редкому расписанию: популярность, похожесть
/// треков и чистку сырых событий по сроку хранения.
///
/// <para>
/// Отделён от пользовательского воркера, потому что профиль затрат другой: это один тяжёлый проход
/// по всей библиотеке раз в несколько часов, а не лёгкий проход на каждого активного слушателя.
/// </para>
/// </summary>
public class LibraryMaintenanceWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<RecommendationOptions> options,
    TimeProvider clock,
    ILogger<LibraryMaintenanceWorker> logger) : BackgroundService
{
    private RecommendationOptions Options => options.Value;

    /// <summary>
    /// Ждёт стартовую задержку, затем по расписанию с периодом <c>SimilarityIntervalHours</c>
    /// запускает проходы обслуживания, пока хост не остановлен.
    /// </summary>
    /// <param name="stoppingToken">Токен остановки хоста.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Options.Enabled)
            return;

        try
        {
            // Достаточно долго после старта, чтобы первый проход не соперничал с миграциями,
            // дозаполнением обложек и тем, что пользователь делает в первые секунды на странице.
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

    /// <summary>
    /// Один проход обслуживания: чистка устаревших сырых событий, пересчёт популярности и таблицы
    /// похожести — и запись результата (успех/ошибка, время) как <see cref="RecommendationRun"/>
    /// для видимости через диагностику.
    /// </summary>
    /// <param name="ct">Токен отмены.</param>
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
