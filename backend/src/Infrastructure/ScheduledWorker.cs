// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure;

public abstract class ScheduledWorker(IServiceScopeFactory scopeFactory, ILogger logger) : BackgroundService
{
    protected abstract TimeSpan StartupDelay { get; }

    protected abstract TimeSpan? Interval { get; }

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
