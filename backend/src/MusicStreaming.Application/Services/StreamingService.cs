// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Services;

public record AudioStreamResult(
    Stream Content,
    string ContentType,
    string DownloadName,
    long Length,
    string ETag);

public record HlsMasterResult(bool Ready, string? Content, string ETag);

public record HlsAssetResult(Stream Content, string ContentType, long Length, string ETag);

public class StreamingService(
    IApplicationDbContext db,
    IMusicStorage storage,
    IHlsStorage hls,
    TranscodeQueue transcodeQueue,
    IMemoryCache memoryCache,
    ILogger<StreamingService> logger)
{
    public async Task<AudioStreamResult> OpenTrackAsync(Guid trackId, CancellationToken ct)
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

        var qualities = new[] { AudioQuality.Low, AudioQuality.Normal, AudioQuality.High }
            .Where(quality => quality <= maxQuality && hls.HlsVariantReady(track.ContentHash, quality))
            .ToList();

        var urgent = qualities.Count == 0;
        QueueHls(track.ContentHash, track.FilePath, AudioQuality.Low, urgent);
        QueueHls(track.ContentHash, track.FilePath, AudioQuality.Normal, urgent: false);
        if (maxQuality == AudioQuality.High)
            QueueHls(track.ContentHash, track.FilePath, AudioQuality.High, urgent: false);

        if (urgent)
            return new HlsMasterResult(false, null, $"\"{track.ContentHash}-hls-preparing\"");

        var playlist = HlsPlaylist.BuildMaster(qualities.Select(quality =>
            (quality, AudioBitrates.For(quality)!.Value)));

        var version = string.Join('-', qualities.Select(q => q.ToString().ToLowerInvariant()));
        return new HlsMasterResult(true, playlist, $"\"{track.ContentHash}-hls-{version}\"");
    }

    public async Task<HlsAssetResult> OpenHlsAssetAsync(
        Guid trackId, AudioQuality quality, string fileName, CancellationToken ct = default)
    {
        if (quality == AudioQuality.Original || !HlsPlaylist.IsAssetFileName(fileName))
            throw new NotFoundException("HLS asset not found.");

        var contentHash = await memoryCache.GetOrCreateAsync(
            RecommendationCacheKeys.TrackHash(trackId),
            async entry =>
            {
                entry.SlidingExpiration = TimeSpan.FromHours(1);
                return await db.Tracks.AsNoTracking()
                    .Where(t => t.Id == trackId)
                    .Select(t => t.ContentHash)
                    .FirstOrDefaultAsync(ct);
            })
            ?? throw new NotFoundException("Track not found.");

        var content = hls.OpenHlsFile(contentHash, quality, fileName)
            ?? throw new NotFoundException("HLS asset not found.");

        var contentType = fileName.EndsWith(".m3u8", StringComparison.Ordinal)
            ? "application/vnd.apple.mpegurl"
            : "audio/mp4";

        return new HlsAssetResult(
            content,
            contentType,
            content.Length,
            $"\"{contentHash}-hls-{quality.ToString().ToLowerInvariant()}-{fileName}\"");
    }

    private void QueueHls(string contentHash, string filePath, AudioQuality quality, bool urgent)
    {
        if (hls.HlsVariantReady(contentHash, quality))
            return;

        var request = new TranscodeRequest(contentHash, filePath, quality);

        if (urgent)
            transcodeQueue.TryEnqueueUrgent(request);
        else
            transcodeQueue.TryEnqueueWarmup(request);
    }
}
