using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Domain.Common;

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

public record CoverResult(Stream Content, string ContentType, string ETag) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public class StreamingService(
    IApplicationDbContext db,
    IMusicStorage storage,
    ICurrentUser currentUser,
    IAudioTranscoder transcoder,
    TranscodeQueue transcodeQueue,
    UserSettingsService settings,
    ILogger<StreamingService> logger)
{
    public async Task<AudioStreamResult> OpenTrackAsync(
        Guid trackId, AudioQuality? quality = null, CancellationToken ct = default)
    {
        var track = await db.Tracks.AsNoTracking()
            .Where(t => t.Id == trackId)
            .Select(t => new
            {
                t.FilePath,
                t.MimeType,
                t.OriginalFileName,
                t.ContentHash,
                t.Title,
                ArtistName = t.Artist!.Name,
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Track not found.");

        var wanted = quality ?? (await settings.GetAsync(ct)).EffectiveQuality;

        if (wanted != AudioQuality.Original && transcoder.IsAvailable)
        {
            var cached = storage.OpenRead(storage.TranscodePathFor(track.ContentHash, wanted));

            if (cached is not null)
            {
                return new AudioStreamResult(
                    cached,
                    OpusContentType,
                    DownloadFileName.For(track.ArtistName, track.Title, OpusExtension),
                    cached.Length,
                    $"\"{track.ContentHash}-{wanted}\"");
            }

            transcodeQueue.TryEnqueue(new TranscodeRequest(track.ContentHash, track.FilePath, wanted));
        }

        var stream = storage.OpenRead(track.FilePath);
        if (stream is null)
        {
            logger.LogError(
                "Track {TrackId} is registered at {FilePath} but the file is missing from storage",
                trackId, track.FilePath);
            throw new NotFoundException("The audio file for this track is missing from storage.");
        }

        var extension = Path.GetExtension(track.OriginalFileName) is { Length: > 0 } fromUpload
            ? fromUpload
            : ".mp3";

        return new AudioStreamResult(
            stream,
            track.MimeType,
            DownloadFileName.For(track.ArtistName, track.Title, extension),
            stream.Length,
            $"\"{track.ContentHash}\"");
    }

    public async Task<CoverResult> OpenAlbumCoverAsync(
        Guid albumId, CoverSize size = CoverSize.Full, CancellationToken ct = default)
    {
        var coverPath = await db.Albums.AsNoTracking()
            .Where(a => a.Id == albumId)
            .Select(a => a.CoverPath)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(coverPath))
            throw new NotFoundException("This album has no cover art.");

        var requestedPath = storage.CoverVariantPath(coverPath, size);

        return OpenImage(requestedPath, "cover of album", albumId);
    }

    public async Task<CoverResult> OpenArtistImageAsync(Guid artistId, CancellationToken ct = default)
    {
        var imagePath = await db.Artists.AsNoTracking()
            .Where(a => a.Id == artistId)
            .Select(a => a.ImagePath)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(imagePath))
            throw new NotFoundException("This artist has no photo.");

        return OpenImage(imagePath, "photo of artist", artistId);
    }

    public async Task<CoverResult> OpenPlaylistCoverAsync(Guid playlistId, CancellationToken ct = default)
    {
        var coverPath = await db.Playlists.AsNoTracking()
            .Where(p => p.Id == playlistId && (p.UserId == currentUser.Id || p.IsPublic))
            .Select(p => p.CoverPath)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(coverPath))
            throw new NotFoundException("This playlist has no cover art.");

        return OpenImage(coverPath, "cover of playlist", playlistId);
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

        if (stream is null || absolutePath is null)
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

        var stamp = File.GetLastWriteTimeUtc(absolutePath).Ticks;
        return new CoverResult(stream, contentType, $"\"{stamp:x}-{stream.Length:x}\"");
    }

    private const string OpusContentType = "audio/ogg";
    private const string OpusExtension = ".opus";
}
