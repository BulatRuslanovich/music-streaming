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

/// <summary>A single uploaded file, decoupled from ASP.NET's <c>IFormFile</c>.</summary>
public sealed record UploadCandidate(string FileName, string? ContentType, long Length, Func<Stream> OpenReadStream);

public sealed class TrackUploadService(
    IApplicationDbContext db,
    IMusicStorage storage,
    IAudioMetadataReader metadataReader,
    LibraryService library,
    IOptions<StorageOptions> storageOptions,
    ILogger<TrackUploadService> logger)
{
    private static readonly string[] AllowedContentTypes =
        ["audio/mpeg", "audio/mp3", "audio/mpeg3", "audio/x-mpeg-3", "application/octet-stream"];

    private long MaxUploadBytes => storageOptions.Value.MaxUploadBytes;

    /// <summary>
    /// Stores each MP3 and creates its library rows. One bad file fails on its own without
    /// aborting the rest of the batch, so a 50-file drop never has to be retried wholesale.
    /// </summary>
    public async Task<UploadResultDto> UploadAsync(
        IReadOnlyList<UploadCandidate> files,
        CancellationToken ct = default)
    {
        if (files.Count == 0)
            throw new ValidationException("No files were provided.");

        var uploaded = new List<TrackDto>();
        var failed = new List<UploadFailureDto>();

        foreach (var file in files)
        {
            try
            {
                uploaded.Add(await UploadSingleAsync(file, ct));
            }
            catch (AppException ex)
            {
                logger.LogWarning("Upload of {FileName} rejected: {Reason}", file.FileName, ex.Message);
                failed.Add(new UploadFailureDto(file.FileName, ex.Message));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected failure while uploading {FileName}", file.FileName);
                failed.Add(new UploadFailureDto(file.FileName, "The file could not be processed."));
            }
        }

        return new UploadResultDto(uploaded, failed);
    }

    private async Task<TrackDto> UploadSingleAsync(UploadCandidate file, CancellationToken ct)
    {
        ValidateEnvelope(file);

        // Stream straight to its final location: the file never sits in memory in one piece.
        StoredFile stored;
        await using (var input = file.OpenReadStream())
        {
            stored = await storage.SaveTrackAsync(input, MaxUploadBytes, ct);
        }

        try
        {
            if (stored.SizeBytes == 0)
                throw new ValidationException("The file is empty.");

            var duplicate = await db.Tracks
                .Where(t => t.ContentHash == stored.ContentHash)
                .Select(t => t.Id)
                .FirstOrDefaultAsync(ct);

            if (duplicate != Guid.Empty)
                throw new ConflictException("This file is already in the library.");

            var absolutePath = storage.ResolveExisting(stored.RelativePath)
                ?? throw new ValidationException("The uploaded file could not be read back.");

            // Parsing the tags doubles as the integrity check: a file that is not really an MP3
            // fails here, after the cheap extension and size checks have already passed.
            var metadata = metadataReader.Read(absolutePath)
                ?? throw new ValidationException("The file is not a readable MP3.");

            if (metadata.DurationSeconds <= 0)
                throw new ValidationException("The file contains no audio stream.");

            var track = await BuildTrackAsync(file, stored, metadata, ct);
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Uploaded track {TrackId} ({Title}) from {FileName}, {Bytes} bytes",
                track.Id, track.Title, file.FileName, stored.SizeBytes);

            return await library.GetTrackAsync(track.Id, ct);
        }
        catch
        {
            // Nothing was committed, so the file on disk would be unreachable garbage.
            storage.Delete(stored.RelativePath);
            throw;
        }
    }

    private void ValidateEnvelope(UploadCandidate file)
    {
        var extension = Path.GetExtension(file.FileName);
        if (!string.Equals(extension, ".mp3", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Only .mp3 files are supported.");

        if (file.ContentType is not null &&
            !AllowedContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            throw new ValidationException($"Unsupported content type '{file.ContentType}'.");
        }

        if (file.Length > MaxUploadBytes)
            throw new ValidationException($"The file exceeds the {MaxUploadBytes / (1024 * 1024)} MB limit.");
    }

    private async Task<Track> BuildTrackAsync(
        UploadCandidate file, StoredFile stored, AudioMetadata metadata, CancellationToken ct)
    {
        var title = Coalesce(metadata.Title) ?? Path.GetFileNameWithoutExtension(file.FileName);

        // A tag names its performers in one string more often than in separate values, so the
        // artist field is split here: "BONES, Grayera" becomes two artists, both credited.
        var credits = await library.ResolveArtistsAsync(
            metadata.Artists.Count > 0 ? metadata.Artists : metadata.AlbumArtists, ct);

        var trackArtist = credits[0];
        await db.SaveChangesAsync(ct); // assigns the artist ids before they are referenced below

        Album? album = null;
        if (Coalesce(metadata.Album) is { } albumTitle)
        {
            // Compilations credit the album to its album artist, not to each track's artist.
            // Either way one artist owns the album: the first one named.
            var albumArtist = metadata.AlbumArtists.Count > 0
                ? (await library.ResolveArtistsAsync(metadata.AlbumArtists, ct))[0]
                : trackArtist;

            await db.SaveChangesAsync(ct);

            album = await library.GetOrCreateAlbumAsync(albumTitle, albumArtist.Id, metadata.Year, ct);
            await db.SaveChangesAsync(ct);

            await AttachCoverAsync(album, metadata, ct);
        }

        Genre? genre = null;
        if (Coalesce(metadata.Genre) is { } genreName)
        {
            genre = await library.GetOrCreateGenreAsync(genreName, ct);
            await db.SaveChangesAsync(ct);
        }

        var track = new Track
        {
            Title = title,
            NormalizedTitle = Normalize.Key(title),
            ArtistId = trackArtist.Id,
            AlbumId = album?.Id,
            GenreId = genre?.Id,
            TrackNumber = metadata.TrackNumber,
            DiscNumber = metadata.DiscNumber,
            Year = metadata.Year ?? album?.Year,
            DurationSeconds = metadata.DurationSeconds,
            FilePath = stored.RelativePath,
            OriginalFileName = SafeOriginalName(file.FileName),
            MimeType = "audio/mpeg",
            FileSize = stored.SizeBytes,
            ContentHash = stored.ContentHash,
        };

        // Added through the navigation so EF inserts the track before the rows that point at it.
        for (var position = 0; position < credits.Count; position++)
            track.TrackArtists.Add(new TrackArtist { ArtistId = credits[position].Id, Position = position });

        db.Tracks.Add(track);
        return track;
    }

    private async Task AttachCoverAsync(Album album, AudioMetadata metadata, CancellationToken ct)
    {
        if (album.CoverPath is not null || metadata.CoverData is null || metadata.CoverData.Length == 0)
            return;

        album.CoverPath = await storage.SaveCoverAsync(
            album.Id, metadata.CoverData, metadata.CoverMimeType ?? "image/jpeg", ct);

        await db.SaveChangesAsync(ct);
    }

    private static string? Coalesce(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Keeps only the leaf name of what the client sent. The value is display-only metadata,
    /// but stripping directory components keeps a crafted name out of any path built from it.
    /// </summary>
    private static string SafeOriginalName(string fileName)
    {
        var leaf = fileName.Replace('\\', '/').Split('/').Last().Trim();
        return leaf.Length > 260 ? leaf[^260..] : leaf;
    }
}
