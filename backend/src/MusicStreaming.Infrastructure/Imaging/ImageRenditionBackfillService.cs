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

/// <summary>
/// Досоздаёт миниатюры для фото артистов и обложек плейлистов, залитых одним размером.
/// </summary>
/// <remarks>
/// Обложки альбомов этим занимается <see cref="CoverBackfillService"/>, и там источником служит
/// вшитый в теги арт. Здесь источника лучше сохранённого файла нет и быть не может, поэтому
/// работа простая: пересобрать из него те ступени, которые он способен дать. Апскейла не будет —
/// его запрещает сам процессор, так что из файла в 640 получится 640 и 256, а не выдуманная 1024.
///
/// Без этого сетка «Артисты» тянула шестьдесят полноразмерных картинок на шестьдесят кружков
/// по 64 пикселя.
/// </remarks>
public class ImageRenditionBackfillService(
    IServiceScopeFactory scopeFactory,
    IMusicStorage storage,
    IImageProcessor imageProcessor,
    ILogger<ImageRenditionBackfillService> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan PauseBetweenImages = TimeSpan.FromMilliseconds(250);

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
            logger.LogError(ex, "Image rendition backfill stopped unexpectedly");
        }
    }

    private async Task BackfillAsync(CancellationToken ct)
    {
        var pending = await FindOutdatedAsync(ct);
        if (pending.Count == 0)
            return;

        logger.LogInformation(
            "Building missing renditions for {Count} artist photos and playlist covers", pending.Count);

        var converted = 0;
        foreach (var (what, path) in pending)
        {
            ct.ThrowIfCancellationRequested();

            if (await ConvertAsync(what, path, ct))
                converted++;

            await Task.Delay(PauseBetweenImages, ct);
        }

        logger.LogInformation(
            "Image rendition backfill finished: {Converted} of {Count} rebuilt", converted, pending.Count);
    }

    private async Task<IReadOnlyList<(string What, string Path)>> FindOutdatedAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var artistImages = await db.Artists.AsNoTracking()
            .Where(artist => artist.ImagePath != null)
            .Select(artist => artist.ImagePath!)
            .ToListAsync(ct);

        var playlistCovers = await db.Playlists.AsNoTracking()
            .Where(playlist => playlist.CoverPath != null)
            .Select(playlist => playlist.CoverPath!)
            .ToListAsync(ct);

        return
        [
            .. artistImages.Where(NeedsThumb).Select(path => ("photo of artist", path)),
            .. playlistCovers.Where(NeedsThumb).Select(path => ("cover of playlist", path)),
        ];
    }

    private bool NeedsThumb(string path) =>
        storage.ResolveExisting(path) is not null
        && storage.ResolveExisting(storage.CoverVariantPath(path, CoverSize.Thumb)) is null;

    private async Task<bool> ConvertAsync(string what, string basePath, CancellationToken ct)
    {
        var absolutePath = storage.ResolveExisting(basePath);
        if (absolutePath is null)
            return false;

        try
        {
            await using var source = File.OpenRead(absolutePath);
            var renditions = await imageProcessor.ToSquareWebpSetAsync(source, CoverVariants.Edges, ct);

            // Базовый файл переписывать нельзя: на него смотрит колонка в базе, и его размер
            // уже правильный. Досоздаём только то, чего рядом нет.
            foreach (var rendition in renditions.Where(r => r.Edge == CoverVariants.ThumbEdge))
            {
                var target = storage.ResolveForWrite(storage.CoverVariantPath(basePath, CoverSize.Thumb));
                await File.WriteAllBytesAsync(target, rendition.Content, ct);
                return true;
            }

            return false;
        }
        catch (Exception ex) when (ex is ValidationException or IOException)
        {
            logger.LogWarning(ex, "Could not build renditions for the {What} at {Path}", what, basePath);
            return false;
        }
    }
}
