// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Domain.Common;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

public sealed record SavedTrack(Track Track, IReadOnlyList<Guid> NewArtistIds);

public class TrackAssembler(
    IApplicationDbContext db,
    IImageStorage images,
    IImageProcessor imageProcessor,
    TagResolver tags,
    LyricsService lyrics,
    TimeProvider clock,
    ILogger<TrackAssembler> logger)
{
    private const int TagConflictAttempts = 4;

    private readonly List<string> _coversWritten = [];

    public async Task<SavedTrack> SaveAsync(
        UploadCandidate file,
        StoredFile stored,
        AudioMetadata metadata,
        AudioFormat format,
        CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var duplicate = await db.Tracks
                .Where(t => t.ContentHash == stored.ContentHash)
                .Select(t => t.Id)
                .FirstOrDefaultAsync(ct);

            if (duplicate != Guid.Empty)
                throw new ConflictException("This file is already in the library.");

            var track = await BuildAsync(file, stored, metadata, format, ct);
            var newArtistIds = db.ChangeTracker.Entries<Artist>()
                .Where(entry => entry.State == EntityState.Added)
                .Select(entry => entry.Entity.Id)
                .Distinct()
                .ToList();

            try
            {
                await db.SaveChangesAsync(ct);
                return new SavedTrack(track, newArtistIds);
            }
            catch (DbUpdateException) when (attempt < TagConflictAttempts)
            {
                Discard();

                logger.LogDebug(
                    "Retrying {FileName} after losing a race for its artist, album or genre (attempt {Attempt})",
                    file.FileName, attempt);
            }
        }
    }

    public void Discard()
    {
        db.ChangeTracker.Clear();
        tags.Forget();
    }

    public void DeleteWrittenCovers()
    {
        foreach (var coverPath in _coversWritten)
            images.DeleteCover(coverPath);
    }

    public void ForgetWrittenCovers() => _coversWritten.Clear();

    private async Task<Track> BuildAsync(
        UploadCandidate file,
        StoredFile stored,
        AudioMetadata metadata,
        AudioFormat format,
        CancellationToken ct)
    {
        var title = Text.TrimToNull(metadata.Title) ?? Path.GetFileNameWithoutExtension(file.FileName);
        var credits = await tags.ResolveArtistsAsync(
            metadata.Artists.Count > 0 ? metadata.Artists : metadata.AlbumArtists, ct);

        var trackArtist = credits[0];

        Album? album = null;
        if (Text.TrimToNull(metadata.Album) is { } albumTitle)
        {
            var albumArtist = metadata.AlbumArtists.Count > 0
                ? (await tags.ResolveArtistsAsync(metadata.AlbumArtists, ct))[0]
                : trackArtist;

            album = await tags.GetOrCreateAlbumAsync(albumTitle, albumArtist.Id, metadata.Year, ct);

            if (album.CoverPath is null && metadata.CoverData is { Length: > 0 } coverData)
            {
                try
                {
                    using var source = new MemoryStream(coverData, writable: false);
                    var renditions = await imageProcessor.ToSquareWebpSetAsync(source, CoverVariants.Edges, ct);

                    album.CoverPath = await images.SaveCoverAsync(album.Id, renditions, ct);
                    _coversWritten.Add(album.CoverPath);

                    logger.LogInformation(
                        "Cover for album {AlbumId} re-encoded: {OriginalBytes} → {WebpBytes} bytes",
                        album.Id, coverData.Length, renditions.Sum(rendition => rendition.Content.Length));
                }
                catch (ValidationException ex)
                {
                    logger.LogWarning(
                        "Album {AlbumId} stays coverless: the embedded art could not be processed ({Reason})",
                        album.Id, ex.Message);
                }
            }
        }

        Genre? genre = null;
        if (Text.TrimToNull(metadata.Genre) is { } genreName)
            genre = await tags.GetOrCreateGenreAsync(genreName, ct);

        var originalName = file.FileName.Replace('\\', '/').Split('/').Last().Trim();

        var track = new Track
        {
            Title = title,
            NormalizedTitle = Normalize.Key(title),
            ArtistId = trackArtist.Id,
            AlbumId = album?.Id,
            GenreId = genre?.Id,
            TrackNumber = metadata.TrackNumber,
            DiscNumber = metadata.DiscNumber,
            Year = metadata.Year,
            DurationSeconds = metadata.DurationSeconds,
            FilePath = stored.RelativePath,
            OriginalFileName = originalName.Length > 260 ? originalName[^260..] : originalName,
            MimeType = format.MimeType,
            FileSize = stored.SizeBytes,
            ContentHash = stored.ContentHash,
            CreatedAt = clock.GetUtcNow(),

            Codec = metadata.Codec ?? format.Label.ToLowerInvariant(),
            BitrateKbps = metadata.BitrateKbps,
            SampleRateHz = metadata.SampleRateHz,
            BitsPerSample = metadata.BitsPerSample,
        };

        for (var position = 0; position < credits.Count; position++)
            track.TrackArtists.Add(new TrackArtist { ArtistId = credits[position].Id, Position = position });

        db.Tracks.Add(track);
        lyrics.AttachFromMetadata(track.Id, metadata);

        return track;
    }
}
