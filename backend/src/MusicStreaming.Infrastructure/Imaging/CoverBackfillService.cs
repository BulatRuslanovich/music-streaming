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
    IImageStorage images,
    IImageProcessor imageProcessor,
    IAudioMetadataReader metadataReader,
    ILogger<CoverBackfillService> logger) : ScheduledWorker(scopeFactory, logger)
{
    protected override TimeSpan StartupDelay => TimeSpan.FromSeconds(20);
    protected override TimeSpan? Interval => null;
    protected override string Name => "Cover backfill";
    private static readonly TimeSpan PauseBetweenCovers = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Отметка «источник этого альбома крупный рендишен дать не может».
    /// </summary>
    /// <remarks>
    /// Без неё каждый запуск заново перечитывал бы теги у всех альбомов с мелкой вшитой
    /// обложкой — а таких в импортированной фонотеке большинство. Пустой файл рядом с обложкой
    /// вместо колонки в базе: это признак производного файла на диске, а не факт предметной
    /// области, и переживать он должен ровно столько же, сколько сами рендишены.
    /// </remarks>
    private const string NoLargeMarker = ".large.none";

    protected override async Task RunPassAsync(CancellationToken ct)
    {
        var pending = await FindOutdatedCoversAsync(ct);
        if (pending.Count == 0)
            return;

        logger.LogInformation("Re-encoding {Count} album covers into webp renditions", pending.Count);

        var converted = 0;
        foreach (var album in pending)
        {
            ct.ThrowIfCancellationRequested();

            if (await ConvertAsync(album.AlbumId, album.CoverPath, ct))
                converted++;

            await Task.Delay(PauseBetweenCovers, ct);
        }

        logger.LogInformation("Cover backfill finished: {Converted} of {Count} re-encoded", converted, pending.Count);
    }

    private async Task<IReadOnlyList<(Guid AlbumId, string CoverPath)>> FindOutdatedCoversAsync(CancellationToken ct)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var covers = await db.Albums.AsNoTracking()
            .Where(album => album.CoverPath != null)
            .Select(album => new { album.Id, CoverPath = album.CoverPath! })
            .ToListAsync(ct);

        return covers
            .Where(album => NeedsRenditions(album.CoverPath))
            .Select(album => (album.Id, album.CoverPath))
            .ToList();
    }

    private bool NeedsRenditions(string coverPath)
    {
        if (Missing(images.CoverVariantPath(coverPath, CoverSize.Thumb)))
            return true;

        return Missing(images.CoverVariantPath(coverPath, CoverSize.Large))
            && Missing(MarkerPath(coverPath));
    }

    private bool Missing(string relativePath) => storage.ResolveExisting(relativePath) is null;

    private static string MarkerPath(string coverPath) =>
        Path.ChangeExtension(coverPath, null) + NoLargeMarker;

    private async Task<bool> ConvertAsync(Guid albumId, string coverPath, CancellationToken ct)
    {
        // Источник — вшитый арт аудиофайла, а не уже сохранённая обложка. Сохранённая обрезана
        // до 640: пересобирать из неё рендишен в 1024 значило бы растянуть её обратно и выдать
        // мыло за улучшение. Оригинал всё это время лежит в теге трека.
        //
        // Обложку, загруженную руками через AlbumEditService, в тегах не найти — для неё
        // остаётся сохранённый файл. Крупного рендишена из него не выйдет, но недостающая
        // миниатюра соберётся, а ради неё этот сервис изначально и написан.
        var source = await FindEmbeddedArtAsync(albumId, ct) ?? ReadStored(coverPath);
        if (source is null)
        {
            logger.LogWarning("Album {AlbumId} points at {CoverPath}, which is missing from storage", albumId, coverPath);
            return false;
        }

        IReadOnlyList<ResizedImage> renditions;
        try
        {
            using var image = new MemoryStream(source, writable: false);
            renditions = await imageProcessor.ToSquareWebpSetAsync(image, CoverVariants.Edges, ct);
        }
        catch (Exception ex) when (ex is ValidationException or IOException)
        {
            logger.LogWarning(ex, "Could not re-encode the cover of album {AlbumId}", albumId);
            return false;
        }

        var newCoverPath = await images.SaveCoverAsync(albumId, renditions, ct);

        if (Missing(newCoverPath) || Missing(images.CoverVariantPath(newCoverPath, CoverSize.Thumb)))
        {
            logger.LogWarning(
                "Kept the original cover of album {AlbumId}: the re-encoded files are not on disk", albumId);
            return false;
        }

        // Вшитый арт оказался мельче 1024. Это не ошибка, но и повторять эту работу на каждом
        // запуске незачем.
        if (!renditions.Any(rendition => rendition.Edge == CoverVariants.LargeEdge))
            await MarkNoLargeAsync(newCoverPath, ct);

        using var scope = CreateScope();
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

    /// <summary>
    /// Первый трек альбома, из тега которого читается картинка.
    /// </summary>
    private async Task<byte[]?> FindEmbeddedArtAsync(Guid albumId, CancellationToken ct)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var files = await db.Tracks.AsNoTracking()
            .Where(track => track.AlbumId == albumId)
            .OrderBy(track => track.DiscNumber)
            .ThenBy(track => track.TrackNumber)
            .Select(track => track.FilePath)
            .Take(MaxTracksProbed)
            .ToListAsync(ct);

        foreach (var relativePath in files)
        {
            ct.ThrowIfCancellationRequested();

            var absolutePath = storage.ResolveExisting(relativePath);
            if (absolutePath is null || AudioUpload.For(absolutePath) is not { } format)
                continue;

            try
            {
                if (metadataReader.Read(absolutePath, format.MetadataMimeType) is { CoverData: { Length: > 0 } art })
                    return art;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Could not read tags of {Path} while backfilling covers", relativePath);
            }
        }

        return null;
    }

    /// <summary>Сколько треков альбома опрашивать, прежде чем признать его безарточным.</summary>
    private const int MaxTracksProbed = 3;

    private byte[]? ReadStored(string coverPath)
    {
        var absolutePath = storage.ResolveExisting(coverPath);

        return absolutePath is null ? null : File.ReadAllBytes(absolutePath);
    }

    private async Task MarkNoLargeAsync(string coverPath, CancellationToken ct)
    {
        var absolutePath = storage.ResolveForWrite(MarkerPath(coverPath));

        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        await File.WriteAllBytesAsync(absolutePath, [], ct);
    }
}
