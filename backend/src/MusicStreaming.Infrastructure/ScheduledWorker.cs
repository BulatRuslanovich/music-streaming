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

    /// <summary>Есть ли смысл запускаться: выключено настройкой, нет ffmpeg, не настроен провайдер.</summary>
    protected virtual bool ShouldRun() => true;

    protected abstract Task RunPassAsync(CancellationToken ct);

    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!ShouldRun())
            return;

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);

            if (Interval is not { } interval)
            {
                await RunPassAsync(stoppingToken);
                return;
            }

            using var timer = new PeriodicTimer(interval);

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
            logger.LogError(ex, "{Worker} stopped unexpectedly", Name);
        }
    }

    /// <summary>Проход работает на собственной области: контекст БД нельзя держать дольше прохода.</summary>
    protected async Task InScopeAsync<TService>(Func<TService, Task> work)
        where TService : notnull
    {
        using var scope = scopeFactory.CreateScope();
        await work(scope.ServiceProvider.GetRequiredService<TService>());
    }

    protected async Task<TResult> InScopeAsync<TService, TResult>(Func<TService, Task<TResult>> work)
        where TService : notnull
    {
        using var scope = scopeFactory.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<TService>());
    }

    protected IServiceScope CreateScope() => scopeFactory.CreateScope();

    /// <summary>Для того немногого, что берёт фабрику целиком — например, записи итога прохода.</summary>
    protected IServiceScopeFactory Scopes => scopeFactory;
}
