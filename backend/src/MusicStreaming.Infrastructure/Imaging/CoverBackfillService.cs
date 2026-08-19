// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Infrastructure.Persistence;

namespace MusicStreaming.Infrastructure.Imaging;

public class CoverBackfillService(
    IServiceScopeFactory scopeFactory,
    IMusicStorage storage,
    IImageProcessor imageProcessor,
    ILogger<CoverBackfillService> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PauseBetweenCovers = TimeSpan.FromMilliseconds(250);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            await BackfillAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cover backfill stopped unexpectedly");
        }
    }

    private async Task BackfillAsync(CancellationToken ct)
    {
        var pending = await FindOutdatedCoversAsync(ct);
        if (pending.Count == 0)
            return;

        logger.LogInformation("Re-encoding {Count} album covers into webp renditions", pending.Count);

        var converted = 0;
        foreach (var (albumId, coverPath) in pending)
        {
            ct.ThrowIfCancellationRequested();

            if (await ConvertAsync(albumId, coverPath, ct))
                converted++;

            await Task.Delay(PauseBetweenCovers, ct);
        }

        logger.LogInformation("Cover backfill finished: {Converted} of {Count} re-encoded", converted, pending.Count);
    }

    private async Task<IReadOnlyList<(Guid AlbumId, string CoverPath)>> FindOutdatedCoversAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var covers = await db.Albums.AsNoTracking()
            .Where(album => album.CoverPath != null)
            .Select(album => new { album.Id, CoverPath = album.CoverPath! })
            .ToListAsync(ct);

        return covers
            .Where(album => storage.ResolveExisting(storage.CoverVariantPath(album.CoverPath, CoverSize.Thumb)) is null)
            .Select(album => (album.Id, album.CoverPath))
            .ToList();
    }

    private async Task<bool> ConvertAsync(Guid albumId, string coverPath, CancellationToken ct)
    {
        var absolutePath = storage.ResolveExisting(coverPath);
        if (absolutePath is null)
        {
            logger.LogWarning("Album {AlbumId} points at {CoverPath}, which is missing from storage", albumId, coverPath);
            return false;
        }

        IReadOnlyList<ResizedImage> renditions;
        try
        {
            await using var source = File.OpenRead(absolutePath);
            renditions = await imageProcessor.ToSquareWebpSetAsync(source, CoverVariants.Edges, ct);
        }
        catch (Exception ex) when (ex is ValidationException or IOException)
        {
            logger.LogWarning(ex, "Could not re-encode the cover of album {AlbumId}", albumId);
            return false;
        }

        var newCoverPath = await storage.SaveCoverAsync(albumId, renditions, ct);

        if (storage.ResolveExisting(newCoverPath) is null ||
            storage.ResolveExisting(storage.CoverVariantPath(newCoverPath, CoverSize.Thumb)) is null)
        {
            logger.LogWarning(
                "Kept the original cover of album {AlbumId}: the re-encoded files are not on disk", albumId);
            return false;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var album = await db.Albums.FirstOrDefaultAsync(candidate => candidate.Id == albumId, ct);
        if (album is null)
            return false;

        var previousCoverPath = album.CoverPath;
        album.CoverPath = newCoverPath;
        await db.SaveChangesAsync(ct);

        if (previousCoverPath is not null && previousCoverPath != newCoverPath)
            storage.Delete(previousCoverPath);

        return true;
    }
}
