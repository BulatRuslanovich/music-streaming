using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;

namespace MusicStreaming.Application.Services;

public record AudioStreamResult(
    Stream Content,
    string ContentType,
    string DownloadName,
    long Length,
    string ETag) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public record CoverResult(Stream Content, string ContentType, string? ETag = null) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public class StreamingService(
    IApplicationDbContext db,
    IMusicStorage storage,
    ILogger<StreamingService> logger)
{
    public async Task<AudioStreamResult> OpenTrackAsync(Guid trackId, CancellationToken ct = default)
    {
        var track = await db.Tracks.AsNoTracking()
            .Where(t => t.Id == trackId)
            .Select(t => new { t.FilePath, t.MimeType, t.OriginalFileName, t.FileSize, t.ContentHash })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Track not found.");

        var stream = storage.OpenRead(track.FilePath);
        if (stream is null)
        {
            logger.LogError(
                "Track {TrackId} is registered at {FilePath} but the file is missing from storage",
                trackId, track.FilePath);
            throw new NotFoundException("The audio file for this track is missing from storage.");
        }

        return new AudioStreamResult(
            stream, track.MimeType, track.OriginalFileName, stream.Length, $"\"{track.ContentHash}\"");
    }

    public async Task<CoverResult> OpenAlbumCoverAsync(Guid albumId, CancellationToken ct = default)
    {
        var coverPath = await db.Albums.AsNoTracking()
            .Where(a => a.Id == albumId)
            .Select(a => a.CoverPath)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(coverPath))
            throw new NotFoundException("This album has no cover art.");

        var stream = storage.OpenRead(coverPath);
        if (stream is null)
        {
            logger.LogWarning("Cover for album {AlbumId} is missing at {CoverPath}", albumId, coverPath);
            throw new NotFoundException("The cover file is missing from storage.");
        }

        var contentType = Path.GetExtension(coverPath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/jpeg",
        };

        return new CoverResult(stream, contentType);
    }

    public async Task<CoverResult> OpenArtistImageAsync(Guid artistId, CancellationToken ct = default)
    {
        var imagePath = await db.Artists.AsNoTracking()
            .Where(a => a.Id == artistId)
            .Select(a => a.ImagePath)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(imagePath))
            throw new NotFoundException("This artist has no photo.");

        var absolutePath = storage.ResolveExisting(imagePath);
        var stream = absolutePath is null ? null : storage.OpenRead(imagePath);

        if (stream is null || absolutePath is null)
        {
            logger.LogWarning("Photo for artist {ArtistId} is missing at {Path}", artistId, imagePath);
            throw new NotFoundException("The photo file is missing from storage.");
        }

        var stamp = File.GetLastWriteTimeUtc(absolutePath).Ticks;
        return new CoverResult(stream, "image/webp", $"\"{stamp:x}-{stream.Length:x}\"");
    }

    public async Task<CoverResult> OpenTrackCoverAsync(Guid trackId, CancellationToken ct = default)
    {
        var albumId = await db.Tracks.AsNoTracking()
            .Where(t => t.Id == trackId)
            .Select(t => t.AlbumId)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("This track has no cover art.");

        return await OpenAlbumCoverAsync(albumId, ct);
    }
}
