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
/// Держит профили вкуса и полки в согласии с тем, что люди слушают.
///
/// <para>
/// Роллап и генерация идут для одного пользователя подряд и именно в таком порядке, чтобы полка
/// никогда не строилась по профилю, отставшему на одну пачку.
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

    /// <summary>
    /// После стартовой задержки помечает всех пользователей на пересчёт (подхватывает то, что
    /// накопилось, пока сервис не работал), затем по таймеру дебаунса обрабатывает "устоявшихся"
    /// пользователей — тех, чья активность не менялась дольше периода дебаунса.
    /// </summary>
    /// <param name="stoppingToken">Токен остановки хоста.</param>
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
    /// Один раз после старта проходит по всем учётным записям. Проход, прерванный посреди пачки,
    /// оставил отметку позади своих событий, и именно это её подхватывает; когда нового нет, роллап
    /// сводится к одному индексному запросу на пользователя и не стоит ничего.
    /// </summary>
    /// <param name="ct">Токен отмены.</param>
    private async Task QueueEveryUserAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var userIds = await db.Users.AsNoTracking().Select(u => u.Id).ToListAsync(ct);
        var startedAt = clock.GetUtcNow() - TimeSpan.FromSeconds(Options.RegenerationDebounceSeconds);

        foreach (var userId in userIds)
            refreshQueue.MarkDirty(userId, startedAt);
    }

    /// <summary>Забирает из очереди пользователей, чья активность "устоялась" (см. <see cref="RecommendationRefreshQueue.ClaimSettled"/>), и обрабатывает каждого по очереди, не давая сбою одного остановить остальных.</summary>
    /// <param name="ct">Токен отмены.</param>
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
    /// Сначала роллап, затем генерация — в таком порядке и в одной области видимости, чтобы полка
    /// никогда не строилась по профилю, отставшему на пачку.
    ///
    /// <para>
    /// Роллап идёт на каждую активность: он стоит одного индексного запроса, когда нового нет, и
    /// именно от него зависят и статистика, и Last.fm. Генерация — нет: полки живут
    /// <c>CacheTtlHours</c>, и пересобирать их на каждую минуту прослушивания значило бы платить
    /// полный проход по кандидатам за сигнал, который на выдачу почти не влияет, и писать сотню
    /// показов о том, чего пользователь не видел.
    /// </para>
    /// </summary>
    /// <param name="userId">Пользователь, чей профиль и полки обновляются.</param>
    /// <param name="ct">Токен отмены.</param>
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

    /// <summary>
    /// Пора ли перестраивать полки: их нет вовсе или хотя бы одна просрочена.
    ///
    /// <para>
    /// «Просрочена» понимается так же, как на чтении (<c>RecommendationService.LoadShelvesAsync</c>),
    /// — иначе читатель ставил бы пользователя в очередь на пересчёт, а воркер отказывался бы его
    /// делать, и полка не обновлялась бы никогда.
    /// </para>
    /// </summary>
    private async Task<bool> ShelvesNeedRebuildAsync(
        ApplicationDbContext db, Guid userId, CancellationToken ct)
    {
        var earliestExpiry = await db.RecommendationCache.AsNoTracking()
            .Where(c => c.UserId == userId)
            .MinAsync(c => (DateTimeOffset?)c.ExpiresAt, ct);

        return earliestExpiry is not { } expiresAt || expiresAt <= clock.GetUtcNow();
    }

    /// <summary>
    /// Пишет журнал прохода собственным контекстом.
    ///
    /// <para>
    /// Общий с проходом контекст хранит его несохранённые правки, и запись отчёта туда же
    /// закоммитила бы ровно то, от чего проход отказался, — а при повторной неудаче исключение
    /// из <c>finally</c> вдобавок заменило бы собой настоящую причину. Отчёт о неудаче не должен
    /// уметь ни того, ни другого.
    /// </para>
    /// </summary>
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
            // Журнал — это диагностика. Потерять строчку в нём не стоит того, чтобы поверх
            // исходной ошибки прохода прилетела вторая, про запись отчёта о ней.
            logger.LogWarning(ex, "Could not record recommendation run {RunId}", run.Id);
        }
    }
}
