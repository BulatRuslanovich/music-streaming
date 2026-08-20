// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services.Integrations;

public enum EnrichmentStatus
{
    Saved,
    Skipped,
    NotFound,
    Ambiguous,
    Instrumental,
}

public record EnrichmentResult(EnrichmentStatus Status, bool Synced = false);

public class LibraryEnrichment(
    IApplicationDbContext db,
    IArtistImageProvider artistImages,
    ILyricsProvider lyricsProvider,
    IMusicStorage storage,
    IImageProcessor imageProcessor,
    TimeProvider clock)
{
    public async Task<EnrichmentResult> EnrichArtistAsync(Guid artistId, CancellationToken ct = default)
    {
        var artist = await db.Artists.FirstOrDefaultAsync(candidate => candidate.Id == artistId, ct);
        if (artist is null || artist.ImagePath is not null)
            return new EnrichmentResult(EnrichmentStatus.Skipped);

        var found = await artistImages.LookupAsync(artist.Name, ct);
        if (found.Status == ArtistImageLookupStatus.NotFound)
            return new EnrichmentResult(EnrichmentStatus.NotFound);
        if (found.Status == ArtistImageLookupStatus.Ambiguous)
            return new EnrichmentResult(EnrichmentStatus.Ambiguous);
        if (found.Content is not { Length: > 0 } content)
            return new EnrichmentResult(EnrichmentStatus.NotFound);

        using var source = new MemoryStream(content, writable: false);
        var webp = await imageProcessor.ToSquareWebpAsync(source, ImageUpload.Edge, ct);
        var path = await storage.SaveArtistImageAsync(artist.Id, webp, ct);

        try
        {
            artist.ImagePath = path;
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            storage.Delete(path);
            throw;
        }

        return new EnrichmentResult(EnrichmentStatus.Saved);
    }

    public async Task<EnrichmentResult> EnrichLyricsAsync(Guid trackId, CancellationToken ct = default)
    {
        var track = await db.Tracks
            .Include(candidate => candidate.Artist)
            .Include(candidate => candidate.Album)
            .Include(candidate => candidate.Lyrics)
            .FirstOrDefaultAsync(candidate => candidate.Id == trackId, ct);

        if (track is null || track.Lyrics is not null)
            return new EnrichmentResult(EnrichmentStatus.Skipped);

        var query = new LyricsQuery(
            track.Title,
            track.Artist?.Name ?? string.Empty,
            track.Album?.Title,
            track.DurationSeconds);

        var found = await lyricsProvider.LookupAsync(query, ct);
        if (found.Status == LyricsLookupStatus.NotFound)
            return new EnrichmentResult(EnrichmentStatus.NotFound);
        if (found.Status == LyricsLookupStatus.Instrumental)
            return new EnrichmentResult(EnrichmentStatus.Instrumental);

        var parsed = LyricsText.Parse(found.Text);
        if (parsed.IsEmpty)
            return new EnrichmentResult(EnrichmentStatus.NotFound);

        db.TrackLyrics.Add(new TrackLyrics
        {
            TrackId = track.Id,
            Plain = parsed.Plain,
            Synced = parsed.Lines,
            Source = LyricsSource.Provider,
            UpdatedAt = clock.GetUtcNow(),
        });

        await db.SaveChangesAsync(ct);

        return new EnrichmentResult(EnrichmentStatus.Saved, parsed.Lines.Count > 0);
    }
}
