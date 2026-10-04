// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Infrastructure.Imaging;
using Infrastructure.Persistence;
using Infrastructure.Storage;
using App.Common;
using App.Dtos;
using Microsoft.EntityFrameworkCore;
using Domain.Common;
using Domain.Entities;

namespace App.Services;

public class ArtistProfileService(
    ApplicationDbContext db,
    FileSystemImageStorage images,
    ImageSharpImageProcessor imageProcessor,
    ILogger<ArtistProfileService> logger)
{
    public async Task<ArtistDto> RenameAsync(Guid id, UpdateArtistRequest request, CancellationToken ct)
    {
        var artist = await LoadAsync(id, ct);

        var name = request.Name.Trim();

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
            UploadLimits.ImageBytes, ct);

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

        images.DeleteCover(path);
        logger.LogInformation("Photo removed from artist {ArtistId}", id);
    }

    private async Task<Artist> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Artists.FirstOrDefaultAsync(a => a.Id == id, ct)
        ?? throw new NotFoundException("Artist not found.");

    private Task<ArtistDto> ProjectAsync(Guid id, CancellationToken ct) =>
        db.Artists.AsNoTracking().Where(a => a.Id == id).Select(ToDto.Artist).FirstAsync(ct);
}
