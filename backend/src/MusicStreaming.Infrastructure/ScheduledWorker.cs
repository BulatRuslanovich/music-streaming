// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MusicStreaming.Infrastructure;

/// <summary>
/// Фоновая работа по расписанию: подождать после старта, сделать проход — один или по таймеру.
/// </summary>
/// <remarks>
/// Шесть воркеров писали этот <c>ExecuteAsync</c> слово в слово, и один из них при этом терял
/// <c>catch (Exception)</c> — то есть падал молча. Здесь остаётся ровно одно место, где решается,
/// что отмена это не ошибка, а всё прочее должно попасть в лог.
///
/// Очереди (<c>TranscodeWorker</c>, <c>EventIngestWorker</c> и прочие) сюда не относятся: они
/// ждут не таймер, а появление работы, и это другой цикл.
/// </remarks>
public abstract class ScheduledWorker(IServiceScopeFactory scopeFactory, ILogger logger) : BackgroundService
{
    /// <summary>Пауза перед первым проходом — старт не должен соревноваться с обслуживанием запросов.</summary>
    protected abstract TimeSpan StartupDelay { get; }

    /// <summary>Пауза между проходами; <c>null</c> — сделать один проход и остановиться.</summary>
    protected abstract TimeSpan? Interval { get; }

    /// <summary>Имя воркера для сообщения о неожиданной остановке.</summary>
    protected abstract string Name { get; }

    protected abstract Task RunPassAsync(CancellationToken ct);

    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);

            if (Interval is not { } interval)
            {
                await TryRunPassAsync(stoppingToken);
                return;
            }

            using var timer = new PeriodicTimer(interval);

            do
            {
                await TryRunPassAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Worker} stopped unexpectedly", Name);
        }
    }

    /// <summary>
    /// Проход, падение которого не уносит воркер.
    /// </summary>
    /// <remarks>
    /// Раньше исключение из прохода выходило наружу и завершало <c>ExecuteAsync</c>: воркер
    /// оставался мёртвым до перезапуска процесса, и единственным следом была одна строчка в логе.
    /// Одна неудачная выборка на секундном сбое сети означала, что похожесть больше не
    /// пересчитывается — недели напролёт. Повторной попыткой служит следующий тик таймера:
    /// интервалы здесь от минут до часов, отдельный backoff поверх них ничего не добавляет.
    /// </remarks>
    private async Task TryRunPassAsync(CancellationToken ct)
    {
        try
        {
            await RunPassAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Worker} pass failed; the next scheduled pass will retry", Name);
        }
    }

    protected IServiceScope CreateScope() => scopeFactory.CreateScope();
}
