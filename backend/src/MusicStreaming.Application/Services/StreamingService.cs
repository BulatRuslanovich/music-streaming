// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Options;
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

public record HlsMasterResult(bool Ready, string? Content, string ETag);

public record HlsAssetResult(Stream Content, string ContentType, long Length, string ETag) : IAsyncDisposable
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
    IOptions<TranscodeOptions> transcodeOptions,
    StreamingMetrics metrics,
    IMemoryCache memoryCache,
    ILogger<StreamingService> logger)
{
    public bool HlsEnabled => transcoder.IsAvailable;

    public async Task<AudioStreamResult> OpenTrackAsync(
        Guid trackId, AudioQuality? quality, CancellationToken ct)
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

    public async Task<HlsMasterResult> OpenHlsMasterAsync(
        Guid trackId, AudioQuality maxQuality, CancellationToken ct = default)
    {
        if (maxQuality == AudioQuality.Original)
            throw new ValidationException("Original is not an HLS quality cap.");

        var track = await db.Tracks.AsNoTracking()
            .Where(t => t.Id == trackId)
            .Select(t => new { t.ContentHash, t.FilePath })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Track not found.");

        // Достаточно одной готовой вариации. Требовать сразу Low и Normal значило отдавать 202 и
        // ронять клиента на оригинал (медиана 20 МБ FLAC) даже там, где играбельный рендишен уже
        // лежит на диске — а такой была большая часть библиотеки, пока прогрев не догнал.
        var qualities = new[] { AudioQuality.Low, AudioQuality.Normal, AudioQuality.High }
            .Where(quality => quality <= maxQuality && storage.HlsVariantReady(track.ContentHash, quality))
            .ToList();

        // Пока играть нечего — это запрос по требованию и он идёт в приоритетную полосу. Как только
        // хоть одна вариация готова, плеер уже не ждёт, и остальные догоняются как прогрев.
        var urgent = qualities.Count == 0;
        QueueHls(track.ContentHash, track.FilePath, AudioQuality.Low, urgent);
        QueueHls(track.ContentHash, track.FilePath, AudioQuality.Normal, urgent: false);
        if (maxQuality == AudioQuality.High)
            QueueHls(track.ContentHash, track.FilePath, AudioQuality.High, urgent: false);

        if (urgent)
        {
            metrics.RecordPreparing();
            return new HlsMasterResult(false, null, $"\"{track.ContentHash}-hls-preparing\"");
        }

        var playlist = HlsPlaylist.BuildMaster(qualities.Select(quality =>
            (quality, transcodeOptions.Value.BitrateFor(quality)!.Value)));

        var version = string.Join('-', qualities.Select(q => q.ToString().ToLowerInvariant()));
        return new HlsMasterResult(true, playlist, $"\"{track.ContentHash}-hls-{version}\"");
    }

    public async Task<HlsAssetResult> OpenHlsAssetAsync(
        Guid trackId, AudioQuality quality, string fileName, CancellationToken ct = default)
    {
        if (quality == AudioQuality.Original || !HlsPlaylist.IsAssetFileName(fileName))
            throw new NotFoundException("HLS asset not found.");

        // Этот метод вызывается на каждый сегмент — под шестьдесят раз за трек. Связь трека с его
        // content hash неизменна, так что запрос в БД здесь имеет смысл ровно один раз.
        var contentHash = await memoryCache.GetOrCreateAsync(
            $"track-hash:{trackId}",
            async entry =>
            {
                entry.SlidingExpiration = TimeSpan.FromHours(1);
                return await db.Tracks.AsNoTracking()
                    .Where(t => t.Id == trackId)
                    .Select(t => t.ContentHash)
                    .FirstOrDefaultAsync(ct);
            })
            ?? throw new NotFoundException("Track not found.");

        var content = storage.OpenHlsFile(contentHash, quality, fileName)
            ?? throw new NotFoundException("HLS asset not found.");

        var contentType = fileName.EndsWith(".m3u8", StringComparison.Ordinal)
            ? "application/vnd.apple.mpegurl"
            : "audio/mp4";

        if (fileName.EndsWith(".m4s", StringComparison.Ordinal))
            metrics.RecordSegment(quality, content.Length);

        return new HlsAssetResult(
            content,
            contentType,
            content.Length,
            $"\"{contentHash}-hls-{quality.ToString().ToLowerInvariant()}-{fileName}\"");
    }

    private void QueueHls(string contentHash, string filePath, AudioQuality quality, bool urgent)
    {
        if (!transcoder.IsAvailable || storage.HlsVariantReady(contentHash, quality))
            return;

        var request = new TranscodeRequest(contentHash, filePath, quality, TranscodeKind.Hls);

        if (urgent)
            transcodeQueue.TryEnqueue(request);
        else
            transcodeQueue.TryEnqueueWarmup(request);
    }

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

    /// <summary>
    /// Отдаёт ближайший существующий рендишен, спускаясь по ступеням от запрошенного.
    /// </summary>
    /// <remarks>
    /// Крупный рендишен есть не у всякой картинки — у мелкого источника его не из чего сделать,
    /// а фото артистов и обложки плейлистов, залитые до появления рендишенов, лежат одним файлом.
    /// Клиент просит размер, а не конкретный файл, и получать 404 за это он не должен.
    /// </remarks>
    private CoverResult OpenVariant(string basePath, CoverSize size, string what, Guid ownerId)
    {
        var requestedPath = CoverVariants.Ladder(size)
            .Select(step => storage.CoverVariantPath(basePath, step))
            .FirstOrDefault(path => storage.ResolveExisting(path) is not null)
            ?? storage.CoverVariantPath(basePath, size);

        return OpenImage(requestedPath, what, ownerId);
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

    private const string OpusContentType = "audio/ogg";
    private const string OpusExtension = ".opus";
}
