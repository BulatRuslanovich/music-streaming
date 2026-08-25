// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Infrastructure.Storage;

public class LibraryImportWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<LibraryImportOptions> options,
    ILogger<LibraryImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (!settings.Enabled)
            return;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(settings.StartupDelaySeconds), stoppingToken);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.ScanIntervalSeconds));

            do
            {
                await ScanAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task ScanAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var import = scope.ServiceProvider.GetRequiredService<LibraryImportService>();

            await import.ImportAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Library import scan failed");
        }
    }
}
