// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;

namespace MusicStreaming.Application.Services;

public record CoverResult(Stream Content, string ContentType, string ETag);

/// <summary>
/// Байты картинок: обложки альбомов, плейлистов и треков, фотографии артистов.
/// </summary>
/// <remarks>
/// Отдельно от <see cref="StreamingService"/>, который отдаёт звук: у аудио свои очереди,
/// транскодирование и HLS, а здесь всё содержание — найти путь в базе и спуститься по ступеням
/// рендишенов до того файла, который существует. Общего у них только хранилище.
/// </remarks>
public class CoverStreamService(
    IApplicationDbContext db,
    IMusicStorage storage,
    IImageStorage images,
    ICurrentUser currentUser,
    ILogger<CoverStreamService> logger)
{
    public async Task<CoverResult> OpenAlbumCoverAsync(Guid albumId, CoverSize size, CancellationToken ct)
    {
        var coverPath = await db.Albums.AsNoTracking()
            .Where(a => a.Id == albumId)
            .Select(a => a.CoverPath)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(coverPath))
            throw new NotFoundException("This album has no cover art");

        return OpenVariant(coverPath, size, "cover of album", albumId);
    }

    public async Task<CoverResult> OpenArtistImageAsync(
        Guid artistId, CoverSize size = CoverSize.Full, CancellationToken ct = default)
    {
        var imagePath = await db.Artists.AsNoTracking()
            .Where(a => a.Id == artistId)
            .Select(a => a.ImagePath)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(imagePath))
            throw new NotFoundException("This artist has no photo.");

        return OpenVariant(imagePath, size, "photo of artist", artistId);
    }

    public async Task<CoverResult> OpenPlaylistCoverAsync(
        Guid playlistId, CoverSize size = CoverSize.Full, CancellationToken ct = default)
    {
        var coverPath = await db.Playlists.AsNoTracking()
            .Where(p => p.Id == playlistId && (p.UserId == currentUser.Id || p.IsPublic))
            .Select(p => p.CoverPath)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(coverPath))
            throw new NotFoundException("This playlist has no cover art.");

        return OpenVariant(coverPath, size, "cover of playlist", playlistId);
    }

    /// <summary>
    /// Отдаёт ближайший существующий рендишен, спускаясь по ступеням от запрошенного.
    /// </summary>
    /// <remarks>
    /// Крупный рендишен есть не у всякой картинки — у мелкого источника его не из чего сделать,
    /// а фото артистов и обложки плейлистов, залитые до появления рендишенов, лежат одним файлом.
    /// Клиент просит размер, а не конкретный файл, и получать 404 за это он не должен.
    /// </remarks>
    private CoverResult OpenVariant(string basePath, CoverSize size, string what, Guid ownerId)
    {
        var requestedPath = CoverVariants.Ladder(size)
            .Select(step => images.CoverVariantPath(basePath, step))
            .FirstOrDefault(path => storage.ResolveExisting(path) is not null)
            ?? images.CoverVariantPath(basePath, size);

        return OpenImage(requestedPath, what, ownerId);
    }

    public async Task<CoverResult> OpenTrackCoverAsync(
        Guid trackId, CoverSize size = CoverSize.Full, CancellationToken ct = default)
    {
        var albumId = await db.Tracks.AsNoTracking()
            .Where(t => t.Id == trackId)
            .Select(t => t.AlbumId)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("This track has no cover art.");

        return await OpenAlbumCoverAsync(albumId, size, ct);
    }

    private CoverResult OpenImage(string relativePath, string what, Guid ownerId)
    {
        var absolutePath = storage.ResolveExisting(relativePath);
        var stream = absolutePath is null ? null : storage.OpenRead(relativePath);

        if (stream is null)
        {
            logger.LogWarning("The {What} {OwnerId} is missing at {Path}", what, ownerId, relativePath);
            throw new NotFoundException("The image file is missing from storage.");
        }

        var contentType = Path.GetExtension(relativePath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "image/webp",
        };

        var stamp = File.GetLastWriteTimeUtc(absolutePath!).Ticks;
        return new CoverResult(stream, contentType, $"\"{stamp:x}-{stream.Length:x}\"");
    }
}
