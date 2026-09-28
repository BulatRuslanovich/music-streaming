// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Domain.Common;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

public sealed record SavedTrack(Track Track, IReadOnlyList<Guid> NewArtistIds);

/// <summary>
/// Собирает трек из метаданных: разрешает артистов, альбом и жанр, вкладывает обложку и
/// сохраняет всё одной транзакцией.
/// </summary>
/// <remarks>
/// Отделено от приёма байтов, потому что здесь другая единица работы. Загрузка либо записала
/// файл, либо нет; сборка же соревнуется за общие теги с параллельными загрузками, и её нормальный
/// исход — повторить попытку. Заодно список записанных обложек перестал быть состоянием сервиса,
/// живущего дольше одной загрузки.
/// </remarks>
public class TrackAssembler(
    IApplicationDbContext db,
    IImageStorage images,
    IImageProcessor imageProcessor,
    TagResolver tags,
    LyricsService lyrics,
    TimeProvider clock,
    ILogger<TrackAssembler> logger)
{
    private const int TagConflictAttempts = 4;

    private readonly List<string> _coversWritten = [];

    public async Task<SavedTrack> SaveAsync(
        UploadCandidate file,
        StoredFile stored,
        AudioMetadata metadata,
        AudioFormat format,
        CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var duplicate = await db.Tracks
                .Where(t => t.ContentHash == stored.ContentHash)
                .Select(t => t.Id)
                .FirstOrDefaultAsync(ct);

            if (duplicate != Guid.Empty)
                throw new ConflictException("This file is already in the library.");

            var track = await BuildAsync(file, stored, metadata, format, ct);
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
                Discard();

                logger.LogDebug(
                    "Retrying {FileName} after losing a race for its artist, album or genre (attempt {Attempt})",
                    file.FileName, attempt);
            }
        }
    }

    /// <summary>Забыть незавершённую сборку: ничего из неё не должно попасть в следующую попытку.</summary>
    public void Discard()
    {
        db.ChangeTracker.Clear();
        tags.Forget();
    }

    /// <summary>Убрать обложки, записанные сборкой, которая в итоге не сохранилась.</summary>
    public void DeleteWrittenCovers()
    {
        foreach (var coverPath in _coversWritten)
            images.DeleteCover(coverPath);
    }

    public void ForgetWrittenCovers() => _coversWritten.Clear();

    private async Task<Track> BuildAsync(
        UploadCandidate file,
        StoredFile stored,
        AudioMetadata metadata,
        AudioFormat format,
        CancellationToken ct)
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
            Year = metadata.Year,
            DurationSeconds = metadata.DurationSeconds,
            FilePath = stored.RelativePath,
            OriginalFileName = SafeOriginalName(file.FileName),
            MimeType = format.MimeType,
            FileSize = stored.SizeBytes,
            ContentHash = stored.ContentHash,
            CreatedAt = clock.GetUtcNow(),

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

        album.CoverPath = await images.SaveCoverAsync(album.Id, renditions, ct);
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
}
