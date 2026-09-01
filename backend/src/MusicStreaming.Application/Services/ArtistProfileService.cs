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
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

public class ArtistProfileService(
    IApplicationDbContext db,
    IImageStorage images,
    IImageProcessor imageProcessor,
    IOptions<StorageOptions> storageOptions,
    ILogger<ArtistProfileService> logger)
{
    private const int MaxNameLength = 300;

    public async Task<ArtistDto> RenameAsync(Guid id, UpdateArtistRequest request, CancellationToken ct)
    {
        var artist = await LoadAsync(id, ct);

        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            throw new ValidationException("An artist needs a name.");
        else if (name.Length > MaxNameLength)
            throw new ValidationException($"That name is longer than {MaxNameLength} characters.");

        var key = Normalize.Key(name);

        artist.Name = name;
        artist.NormalizedName = key;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException($"An artist named \"{name}\" already exists.");
        }

        logger.LogInformation("Artist {ArtistId} renamed to {Name}", id, name);
        return await ProjectAsync(id, ct);
    }

    public async Task<ArtistDto> SetImageAsync(
        Guid id,
        Stream content,
        string? contentType,
        string fileName,
        long length,
        CancellationToken ct)
    {
        var artist = await LoadAsync(id, ct);

        var renditions = await ImageUpload.AcceptSquareWebpSetAsync(
            imageProcessor, content, contentType, fileName, length,
            storageOptions.Value.MaxImageUploadBytes, ct);

        artist.ImagePath = await images.SaveArtistImageAsync(artist.Id, renditions, ct);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Photo set for artist {ArtistId} ({Renditions} renditions, {Bytes} bytes)",
            id, renditions.Count, renditions.Sum(rendition => rendition.Content.Length));
        return await ProjectAsync(id, ct);
    }

    public async Task RemoveImageAsync(Guid id, CancellationToken ct = default)
    {
        var artist = await LoadAsync(id, ct);

        var path = artist.ImagePath;
        if (path is null)
            return;

        artist.ImagePath = null;
        await db.SaveChangesAsync(ct);

        // У фото теперь есть рендишены, и уносить их надо вместе с базовым файлом.
        images.DeleteCover(path);
        logger.LogInformation("Photo removed from artist {ArtistId}", id);
    }

    private async Task<Artist> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Artists.FirstOrDefaultAsync(a => a.Id == id, ct)
        ?? throw new NotFoundException("Artist not found.");

    private Task<ArtistDto> ProjectAsync(Guid id, CancellationToken ct) =>
        db.Artists.AsNoTracking().Where(a => a.Id == id).Select(ToDto.Artist).FirstAsync(ct);
}
