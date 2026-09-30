// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Recommendations;

namespace MusicStreaming.Application.Services;

public record UploadCandidate(string FileName, string? ContentType, long Length, Func<Stream> OpenReadStream);

public class TrackUploadService(
    IMusicStorage storage,
    IHlsStorage hls,
    IMemoryCache memoryCache,
    IAudioMetadataReader metadataReader,
    CatalogService catalog,
    TrackAssembler assembler,
    TrackPostProcessing postProcessing,
    ILogger<TrackUploadService> logger)
{
    public async Task<UploadResultDto> UploadAsync(UploadCandidate file, CancellationToken ct)
    {
        try
        {
            return new UploadResultDto([await UploadSingleAsync(file, ct)], []);
        }
        catch (AppException ex)
        {
            assembler.Discard();
            logger.LogWarning("Upload of {FileName} rejected: {Reason}", file.FileName, ex.Message);
            return new UploadResultDto([], [new UploadFailureDto(file.FileName, ex.Message)]);
        }
        catch (Exception ex)
        {
            assembler.Discard();
            logger.LogError(ex, "Unexpected failure while uploading {FileName}", file.FileName);
            return new UploadResultDto(
                [], [new UploadFailureDto(file.FileName, "The file could not be processed.")]);
        }
    }

    private async Task<TrackDto> UploadSingleAsync(UploadCandidate file, CancellationToken ct)
    {
        var totalStartedAt = Stopwatch.GetTimestamp();
        var format = AudioUpload.For(file.FileName)
            ?? throw new ValidationException($"Only {AudioUpload.Accepted} files are supported.");

        if (file.Length > UploadLimits.AudioBytes)
            throw new ValidationException($"The file exceeds the {UploadLimits.AudioBytes / (1024 * 1024)} MB limit.");

        var storageStartedAt = Stopwatch.GetTimestamp();
        StoredFile stored;
        await using (var input = file.OpenReadStream())
        {
            stored = await storage.SaveTrackAsync(input, format.Extension, UploadLimits.AudioBytes, ct);
        }
        var storageFinishedAt = Stopwatch.GetTimestamp();

        assembler.ForgetWrittenCovers();

        try
        {
            if (stored.SizeBytes == 0)
                throw new ValidationException("The file is empty.");

            var absolutePath = storage.ResolveExisting(stored.RelativePath)
                ?? throw new ValidationException("The uploaded file could not be read back.");

            var metadataStartedAt = Stopwatch.GetTimestamp();

            if (AudioUpload.SniffContainer(absolutePath) is { } actual && actual != format.Extension)
                throw new ValidationException($"The file is not a {format.Label} file despite its name.");

            var metadata = metadataReader.Read(absolutePath, format.MetadataMimeType)
                ?? throw new ValidationException($"The file is not a readable {format.Label} file.");

            if (metadata.DurationSeconds <= 0)
                throw new ValidationException("The file contains no audio stream.");
            var metadataFinishedAt = Stopwatch.GetTimestamp();

            var persistenceStartedAt = Stopwatch.GetTimestamp();
            var saved = await assembler.SaveAsync(file, stored, metadata, format, ct);
            var track = saved.Track;
            var persistenceFinishedAt = Stopwatch.GetTimestamp();

            if (saved.Replaced is { } replaced)
            {
                storage.Delete(replaced.FilePath);
                hls.DeleteTranscodes(replaced.ContentHash);
                memoryCache.Remove(RecommendationCacheKeys.TrackHash(track.Id));
            }

            postProcessing.Schedule(track, saved.NewArtistIds);

            var projectionStartedAt = Stopwatch.GetTimestamp();
            var result = await catalog.GetTrackAsync(track.Id, ct);
            var finishedAt = Stopwatch.GetTimestamp();

            logger.LogInformation(
                "Uploaded track {TrackId} ({Title}) from {FileName}, {Codec}, {Bytes} bytes in {TotalMs:0} ms "
                + "(stream+hash {StorageMs:0}, metadata {MetadataMs:0}, tags+cover+db {PersistenceMs:0}, "
                + "projection {ProjectionMs:0})",
                track.Id,
                track.Title,
                file.FileName,
                track.Codec,
                stored.SizeBytes,
                ElapsedMilliseconds(totalStartedAt, finishedAt),
                ElapsedMilliseconds(storageStartedAt, storageFinishedAt),
                ElapsedMilliseconds(metadataStartedAt, metadataFinishedAt),
                ElapsedMilliseconds(persistenceStartedAt, persistenceFinishedAt),
                ElapsedMilliseconds(projectionStartedAt, finishedAt));

            return result;
        }
        catch
        {
            storage.Delete(stored.RelativePath);
            assembler.DeleteWrittenCovers();
            throw;
        }
        finally
        {
            assembler.ForgetWrittenCovers();
        }
    }

    private static double ElapsedMilliseconds(long startedAt, long finishedAt) =>
        Stopwatch.GetElapsedTime(startedAt, finishedAt).TotalMilliseconds;
}
