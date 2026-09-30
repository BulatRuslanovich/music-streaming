// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;

namespace MusicStreaming.Application.Services;

public record CoverResult(Stream Content, string ContentType, string ETag);

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

    private CoverResult OpenVariant(string basePath, CoverSize size, string what, Guid ownerId)
    {
        CoverSize[] ladder = size switch
        {
            CoverSize.Large => [CoverSize.Large, CoverSize.Full, CoverSize.Thumb],
            CoverSize.Thumb => [CoverSize.Thumb, CoverSize.Full],
            _ => [CoverSize.Full, CoverSize.Thumb],
        };

        var relativePath = ladder
            .Select(step => images.CoverVariantPath(basePath, step))
            .FirstOrDefault(path => storage.ResolveExisting(path) is not null)
            ?? images.CoverVariantPath(basePath, size);

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
}
