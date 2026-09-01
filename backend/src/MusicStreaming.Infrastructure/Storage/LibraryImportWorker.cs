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
    ILogger<LibraryImportWorker> logger) : ScheduledWorker(scopeFactory, logger)
{
    private LibraryImportOptions Settings => options.Value;

    protected override TimeSpan StartupDelay => TimeSpan.FromSeconds(Settings.StartupDelaySeconds);
    protected override TimeSpan? Interval => TimeSpan.FromSeconds(Settings.ScanIntervalSeconds);
    protected override string Name => "Library import";

    protected override bool ShouldRun() => Settings.Enabled;

    protected override async Task RunPassAsync(CancellationToken ct)
    {
        try
        {
            using var scope = CreateScope();
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
