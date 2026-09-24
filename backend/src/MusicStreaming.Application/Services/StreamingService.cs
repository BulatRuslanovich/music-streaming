// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Services;

/// <remarks>
/// Поток отдаётся дальше в <c>FileStreamResult</c>, и закрывает его MVC — поэтому здесь нет
/// <c>IAsyncDisposable</c>. Он тут был, но его не вызывал никто: обещание, которого никто не
/// исполнял, хуже отсутствия обещания.
/// </remarks>
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
    IAudioTranscoder transcoder,
    TranscodeQueue transcodeQueue,
    UserSettingsService settings,
    IOptions<TranscodeOptions> transcodeOptions,
    StreamingMetrics metrics,
    IMemoryCache memoryCache,
    ILogger<StreamingService> logger)
{
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
            var cached = storage.OpenRead(hls.TranscodePathFor(track.ContentHash, wanted));

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
        if (!transcoder.IsAvailable)
            throw new ServiceUnavailableException("HLS is unavailable.");

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
            .Where(quality => quality <= maxQuality && hls.HlsVariantReady(track.ContentHash, quality))
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
        if (!transcoder.IsAvailable || hls.HlsVariantReady(contentHash, quality))
            return;

        var request = new TranscodeRequest(contentHash, filePath, quality, TranscodeKind.Hls);

        if (urgent)
            transcodeQueue.TryEnqueue(request);
        else
            transcodeQueue.TryEnqueueWarmup(request);
    }

    private const string OpusContentType = "audio/ogg";
    private const string OpusExtension = ".opus";
}
