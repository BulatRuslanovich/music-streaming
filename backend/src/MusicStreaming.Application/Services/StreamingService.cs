using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;

namespace MusicStreaming.Application.Services;

/// <summary>An open read stream plus the headers the API needs to answer a range request.</summary>
public sealed record AudioStreamResult(
    Stream Content,
    string ContentType,
    string DownloadName,
    long Length,
    string ETag) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public sealed record CoverResult(Stream Content, string ContentType) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public sealed class StreamingService(
    IApplicationDbContext db,
    IMusicStorage storage,
    ILogger<StreamingService> logger)
{
    /// <summary>
    /// Opens a track for playback. The stream is handed to ASP.NET, which serves it with range
    /// support straight from the filesystem — the file is never buffered in memory.
    /// </summary>
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

        // The stored file is content-addressed, so its hash is a strong, stable ETag.
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

    /// <summary>Cover art for a track, resolved through its album.</summary>
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
