// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Options;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Services;

public class AlbumEditService(
    IApplicationDbContext db,
    IMusicStorage storage,
    IImageProcessor imageProcessor,
    TagResolver tags,
    IOptions<StorageOptions> storageOptions,
    ILogger<AlbumEditService> logger)
{
    private const int MaxTitleLength = 300;
    private const int EarliestYear = 1500;

    public async Task<AlbumDto> UpdateAsync(Guid id, UpdateAlbumRequest request, CancellationToken ct)
    {
        var album = await db.Albums.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException("Album not found.");

        if (request.Title is not null)
        {
            var title = request.Title.Trim();

            if (title.Length == 0)
                throw new ValidationException("An album needs a title.");
            if (title.Length > MaxTitleLength)
                throw new ValidationException($"That title is longer than {MaxTitleLength} characters.");

            album.Title = title;
            album.NormalizedTitle = Normalize.Key(title);
        }

        if (!string.IsNullOrWhiteSpace(request.Artist))
        {
            var artists = await tags.ResolveArtistsAsync([request.Artist], ct);
            album.ArtistId = artists[0].Id;
        }

        if (request.Year is { } year)
        {
            if (year is < EarliestYear or > 2999)
                throw new ValidationException($"A release year must be between {EarliestYear} and 2999.");

            album.Year = year;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException($"\"{album.Title}\" already exists for that artist.");
        }

        logger.LogInformation("Album {AlbumId} metadata updated", id);
        return await ProjectAsync(id, ct);
    }

    public async Task<AlbumDto> SetCoverAsync(
        Guid id,
        Stream content,
        string? contentType,
        string fileName,
        long length,
        CancellationToken ct)
    {
        var album = await db.Albums.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException("Album not found.");

        ImageUpload.Validate(contentType, fileName, length, storageOptions.Value.MaxImageUploadBytes);

        using var buffered = new MemoryStream();
        await content.CopyToAsync(buffered, ct);
        buffered.Position = 0;

        var renditions = await imageProcessor.ToSquareWebpSetAsync(buffered, CoverVariants.Edges, ct);

        album.CoverPath = await storage.SaveCoverAsync(album.Id, renditions, ct);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Cover set for album {AlbumId} ({Bytes} bytes across {Variants} variants)",
            id, renditions.Sum(rendition => rendition.Content.Length), renditions.Count);

        return await ProjectAsync(id, ct);
    }

    public async Task RemoveCoverAsync(Guid id, CancellationToken ct = default)
    {
        var album = await db.Albums.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException("Album not found.");

        var path = album.CoverPath;
        if (path is null)
            return;

        album.CoverPath = null;
        await db.SaveChangesAsync(ct);

        storage.DeleteCover(path);
        logger.LogInformation("Cover removed from album {AlbumId}", id);
    }

    private Task<AlbumDto> ProjectAsync(Guid id, CancellationToken ct) =>
        db.Albums.AsNoTracking().Where(a => a.Id == id).Select(ToDto.Album).FirstAsync(ct);
}
