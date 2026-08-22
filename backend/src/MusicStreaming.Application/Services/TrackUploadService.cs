// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Services.Integrations;
using MusicStreaming.Domain.Common;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

public record UploadCandidate(string FileName, string? ContentType, long Length, Func<Stream> OpenReadStream);

public class TrackUploadService(
    IApplicationDbContext db,
    IMusicStorage storage,
    IAudioMetadataReader metadataReader,
    IImageProcessor imageProcessor,
    TagResolver tags,
    CatalogService catalog,
    LyricsService lyrics,
    TranscodeQueue transcodeQueue,
    AudioAnalysisQueue audioAnalysisQueue,
    IAudioTranscoder transcoder,
    LibraryEnrichmentQueue enrichmentQueue,
    IOptions<StorageOptions> storageOptions,
    ILogger<TrackUploadService> logger)
{
    private const int TagConflictAttempts = 4;

    private long MaxUploadBytes => storageOptions.Value.MaxUploadBytes;

    public async Task<UploadResultDto> UploadAsync(UploadCandidate file, CancellationToken ct)
    {
        try
        {
            return new UploadResultDto([await UploadSingleAsync(file, ct)], []);
        }
        catch (AppException ex)
        {
            DiscardPending();
            logger.LogWarning("Upload of {FileName} rejected: {Reason}", file.FileName, ex.Message);
            return new UploadResultDto([], [new UploadFailureDto(file.FileName, ex.Message)]);
        }
        catch (Exception ex)
        {
            DiscardPending();
            logger.LogError(ex, "Unexpected failure while uploading {FileName}", file.FileName);
            return new UploadResultDto(
                [], [new UploadFailureDto(file.FileName, "The file could not be processed.")]);
        }
    }

    private void DiscardPending()
    {
        db.ChangeTracker.Clear();
        tags.Forget();
    }

    private async Task<TrackDto> UploadSingleAsync(UploadCandidate file, CancellationToken ct)
    {
        var totalStartedAt = Stopwatch.GetTimestamp();
        var format = ValidateEnvelope(file);

        var storageStartedAt = Stopwatch.GetTimestamp();
        StoredFile stored;
        await using (var input = file.OpenReadStream())
        {
            stored = await storage.SaveTrackAsync(input, format.Extension, MaxUploadBytes, ct);
        }
        var storageFinishedAt = Stopwatch.GetTimestamp();

        _coversWritten.Clear();

        try
        {
            if (stored.SizeBytes == 0)
                throw new ValidationException("The file is empty.");

            var absolutePath = storage.ResolveExisting(stored.RelativePath)
                ?? throw new ValidationException("The uploaded file could not be read back.");

            var metadataStartedAt = Stopwatch.GetTimestamp();

            if (AudioUpload.SniffContainer(absolutePath) is { } actual && actual != format.Extension)
                throw new ValidationException($"The file is not a {format.Label} file despite its name.");

            var metadata = metadataReader.Read(absolutePath, format.TagLibMimeType)
                ?? throw new ValidationException($"The file is not a readable {format.Label} file.");

            if (metadata.DurationSeconds <= 0)
                throw new ValidationException("The file contains no audio stream.");
            var metadataFinishedAt = Stopwatch.GetTimestamp();

            var persistenceStartedAt = Stopwatch.GetTimestamp();
            var saved = await SaveTrackAsync(file, stored, metadata, format, ct);
            var track = saved.Track;
            var persistenceFinishedAt = Stopwatch.GetTimestamp();

            PrepareUnplayableOriginal(track);
            PrepareAdaptiveStreams(track);
            audioAnalysisQueue.TryEnqueue(track.Id);
            enrichmentQueue.TryEnqueue(new LibraryEnrichmentRequest(track.Id, saved.NewArtistIds));

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

            foreach (var coverPath in _coversWritten)
                storage.DeleteCover(coverPath);

            throw;
        }
        finally
        {
            _coversWritten.Clear();
        }
    }

    private readonly List<string> _coversWritten = [];

    private AudioFormat ValidateEnvelope(UploadCandidate file)
    {
        var format = AudioUpload.For(file.FileName)
            ?? throw new ValidationException($"Only {AudioUpload.Accepted} files are supported.");

        if (file.Length > MaxUploadBytes)
            throw new ValidationException($"The file exceeds the {MaxUploadBytes / (1024 * 1024)} MB limit.");

        return format;
    }

    private async Task<SavedTrack> SaveTrackAsync(
        UploadCandidate file, StoredFile stored, AudioMetadata metadata, AudioFormat format, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var duplicate = await db.Tracks
                .Where(t => t.ContentHash == stored.ContentHash)
                .Select(t => t.Id)
                .FirstOrDefaultAsync(ct);

            if (duplicate != Guid.Empty)
                throw new ConflictException("This file is already in the library.");

            var track = await BuildTrackAsync(file, stored, metadata, format, ct);
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
                DiscardPending();

                logger.LogDebug(
                    "Retrying {FileName} after losing a race for its artist, album or genre (attempt {Attempt})",
                    file.FileName, attempt);
            }
        }
    }

    private sealed record SavedTrack(Track Track, IReadOnlyList<Guid> NewArtistIds);

    private void PrepareUnplayableOriginal(Track track)
    {
        if (track.Codec is not "alac" || !transcoder.IsAvailable)
            return;

        transcodeQueue.TryEnqueue(new TranscodeRequest(track.ContentHash, track.FilePath, AudioQuality.Normal));
    }

    private void PrepareAdaptiveStreams(Track track)
    {
        if (!transcoder.IsAvailable)
            return;

        transcodeQueue.TryEnqueue(new TranscodeRequest(
            track.ContentHash, track.FilePath, AudioQuality.Low, TranscodeKind.Hls));
        transcodeQueue.TryEnqueue(new TranscodeRequest(
            track.ContentHash, track.FilePath, AudioQuality.Normal, TranscodeKind.Hls));
    }

    private async Task<Track> BuildTrackAsync(
        UploadCandidate file, StoredFile stored, AudioMetadata metadata, AudioFormat format, CancellationToken ct)
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

            await AttachCoverAsync(album, metadata, ct);
        }

        Genre? genre = null;
        if (Text.TrimToNull(metadata.Genre) is { } genreName)
            genre = await tags.GetOrCreateGenreAsync(genreName, ct);

        var track = new Track
        {
            Title = title,
            NormalizedTitle = Normalize.Key(title),
            ArtistId = trackArtist.Id,
            AlbumId = album?.Id,
            GenreId = genre?.Id,
            TrackNumber = metadata.TrackNumber,
            DiscNumber = metadata.DiscNumber,
            // The album's year is the album's: a track claims only the year its own tags carry.
            Year = metadata.Year,
            DurationSeconds = metadata.DurationSeconds,
            FilePath = stored.RelativePath,
            OriginalFileName = SafeOriginalName(file.FileName),
            MimeType = format.MimeType,
            FileSize = stored.SizeBytes,
            ContentHash = stored.ContentHash,

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

    private async Task AttachCoverAsync(Album album, AudioMetadata metadata, CancellationToken ct)
    {
        if (album.CoverPath is not null || metadata.CoverData is null || metadata.CoverData.Length == 0)
            return;

        IReadOnlyList<ResizedImage> renditions;
        try
        {
            using var source = new MemoryStream(metadata.CoverData, writable: false);
            renditions = await imageProcessor.ToSquareWebpSetAsync(source, CoverVariants.Edges, ct);
        }
        catch (ValidationException ex)
        {
            logger.LogWarning(
                "Album {AlbumId} stays coverless: the embedded art could not be processed ({Reason})",
                album.Id, ex.Message);
            return;
        }

        album.CoverPath = await storage.SaveCoverAsync(album.Id, renditions, ct);
        _coversWritten.Add(album.CoverPath);

        logger.LogInformation(
            "Cover for album {AlbumId} re-encoded: {OriginalBytes} → {WebpBytes} bytes",
            album.Id, metadata.CoverData.Length, renditions.Sum(rendition => rendition.Content.Length));
    }

    private static string SafeOriginalName(string fileName)
    {
        var leaf = fileName.Replace('\\', '/').Split('/').Last().Trim();
        return leaf.Length > 260 ? leaf[^260..] : leaf;
    }

    private static double ElapsedMilliseconds(long startedAt, long finishedAt) =>
        Stopwatch.GetElapsedTime(startedAt, finishedAt).TotalMilliseconds;
}
