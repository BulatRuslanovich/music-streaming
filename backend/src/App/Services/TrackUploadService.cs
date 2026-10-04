// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Infrastructure.Metadata;
using Infrastructure.Storage;
using System.Diagnostics;
using App.Common;
using App.Dtos;
using Microsoft.Extensions.Caching.Memory;

namespace App.Services;

public record UploadCandidate(string FileName, string? ContentType, long Length, Func<Stream> OpenReadStream);

public class TrackUploadService(
    FileSystemMusicStorage storage,
    FileSystemHlsStorage hls,
    IMemoryCache memoryCache,
    TagLibAudioMetadataReader metadataReader,
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
            logger.LogInformation("Upload of {FileName} rejected: {Reason}", file.FileName, ex.Message);
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
        var startedAt = Stopwatch.GetTimestamp();
        var format = AudioUpload.For(file.FileName)
            ?? throw new ValidationException($"Only {AudioUpload.Accepted} files are supported.");

        if (file.Length > UploadLimits.AudioBytes)
            throw new ValidationException($"The file exceeds the {UploadLimits.AudioBytes / (1024 * 1024)} MB limit.");

        StoredFile stored;
        await using (var input = file.OpenReadStream())
        {
            stored = await storage.SaveTrackAsync(input, format.Extension, UploadLimits.AudioBytes, ct);
        }

        assembler.ForgetWrittenCovers();

        try
        {
            if (stored.SizeBytes == 0)
                throw new ValidationException("The file is empty.");

            var absolutePath = storage.ResolveExisting(stored.RelativePath)
                ?? throw new ValidationException("The uploaded file could not be read back.");

            if (AudioUpload.SniffContainer(absolutePath) is { } actual && actual != format.Extension)
                throw new ValidationException($"The file is not a {format.Label} file despite its name.");

            var metadata = metadataReader.Read(absolutePath, format.MetadataMimeType)
                ?? throw new ValidationException($"The file is not a readable {format.Label} file.");

            if (metadata.DurationSeconds <= 0)
                throw new ValidationException("The file contains no audio stream.");

            var saved = await assembler.SaveAsync(file, stored, metadata, format, ct);
            var track = saved.Track;

            if (saved.Replaced is { } replaced)
            {
                storage.Delete(replaced.FilePath);
                hls.DeleteTranscodes(replaced.ContentHash);
                memoryCache.Remove(StreamingService.TrackHashCacheKey(track.Id));
            }

            postProcessing.Schedule(track, saved.NewArtistIds);

            var result = await catalog.GetTrackAsync(track.Id, ct);

            logger.LogInformation(
                "Uploaded {FileName} as track {TrackId}: {Codec}, {Megabytes:0.0} MB in {Elapsed:0.0} s",
                file.FileName,
                track.Id,
                track.Codec,
                stored.SizeBytes / (1024.0 * 1024.0),
                Stopwatch.GetElapsedTime(startedAt).TotalSeconds);

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
}
