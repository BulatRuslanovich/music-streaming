// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Infrastructure.Imaging;
using Infrastructure.Metadata;
using Infrastructure.Persistence;
using Infrastructure.Storage;
using App.Common;
using Microsoft.EntityFrameworkCore;
using Domain.Common;
using Domain.Entities;

namespace App.Services;

public sealed record ReplacedFile(string FilePath, string ContentHash);

public sealed record SavedTrack(Track Track, IReadOnlyList<Guid> NewArtistIds, ReplacedFile? Replaced = null);

public class TrackAssembler(
    ApplicationDbContext db,
    FileSystemImageStorage images,
    ImageSharpImageProcessor imageProcessor,
    TagResolver tags,
    LyricsService lyrics,
    TimeProvider clock,
    ILogger<TrackAssembler> logger)
{
    private const int TagConflictAttempts = 4;
    private const int SameRecordingToleranceSeconds = 3;

    private readonly List<string> _coversWritten = [];

    public async Task<SavedTrack> SaveAsync(
        UploadCandidate file,
        StoredFile stored,
        AudioMetadata metadata,
        AudioFormat format,
        CancellationToken ct)
    {
        if (Text.TrimToNull(metadata.Title) is null && metadata.Artists.Count == 0 && metadata.AlbumArtists.Count == 0)
        {
            var parts = Path.GetFileNameWithoutExtension(file.FileName)
                .Replace('_', ' ')
                .Split([" - ", " – ", " — "], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .SkipWhile(part => part.All(char.IsAsciiDigit))
                .ToList();

            if (parts.Count >= 2)
                metadata = metadata with { Artists = [parts[0]], Title = string.Join(" - ", parts.Skip(1)) };
        }

        for (var attempt = 1; ; attempt++)
        {
            var duplicate = await db.Tracks
                .Where(t => t.ContentHash == stored.ContentHash)
                .Select(t => t.Id)
                .FirstOrDefaultAsync(ct);

            if (duplicate != Guid.Empty)
                throw new ConflictException("This file is already in the library.");

            var codec = metadata.Codec ?? format.Label.ToLowerInvariant();
            var title = Text.TrimToNull(metadata.Title) ?? Path.GetFileNameWithoutExtension(file.FileName);
            var leafName = file.FileName.Replace('\\', '/').Split('/').Last().Trim();
            var originalName = leafName.Length > 260 ? leafName[^260..] : leafName;
            var titleKey = Normalize.Key(title);
            var names = metadata.Artists.Count > 0 ? metadata.Artists : metadata.AlbumArtists;
            List<string> artistKeys = [.. names.Concat(names.SelectMany(ArtistNames.Split)).Select(Normalize.Key).Distinct()];
            var shortest = metadata.DurationSeconds - SameRecordingToleranceSeconds;
            var longest = metadata.DurationSeconds + SameRecordingToleranceSeconds;

            var sameRecording = await db.Tracks
                .Where(t => t.NormalizedTitle == titleKey
                            && t.DurationSeconds >= shortest
                            && t.DurationSeconds <= longest
                            && t.TrackArtists.Any(ta => artistKeys.Contains(ta.Artist!.NormalizedName)))
                .ToListAsync(ct);

            if (!AudioUpload.IsLossless(codec) && sameRecording.Any(t => AudioUpload.IsLossless(t.Codec)))
                throw new ConflictException("A lossless version of this track is already in the library.");

            if (AudioUpload.IsLossless(codec) && sameRecording.Find(t => !AudioUpload.IsLossless(t.Codec)) is { } lossy)
            {
                var replaced = new ReplacedFile(lossy.FilePath, lossy.ContentHash);

                lossy.FilePath = stored.RelativePath;
                lossy.OriginalFileName = originalName;
                lossy.MimeType = format.MimeType;
                lossy.FileSize = stored.SizeBytes;
                lossy.ContentHash = stored.ContentHash;
                lossy.DurationSeconds = metadata.DurationSeconds;
                lossy.Codec = codec;
                lossy.BitrateKbps = metadata.BitrateKbps;
                lossy.SampleRateHz = metadata.SampleRateHz;
                lossy.BitsPerSample = metadata.BitsPerSample;

                await db.SaveChangesAsync(ct);

                logger.LogInformation(
                    "Track {TrackId} ({Title}) upgraded to {Codec} from {FileName}", lossy.Id, lossy.Title, codec, file.FileName);

                return new SavedTrack(lossy, [], replaced);
            }

            var track = await BuildAsync(title, originalName, codec, stored, metadata, format, ct);
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

    public void Discard()
    {
        db.ChangeTracker.Clear();
        tags.Forget();
    }

    public void DeleteWrittenCovers()
    {
        foreach (var coverPath in _coversWritten)
            images.DeleteCover(coverPath);
    }

    public void ForgetWrittenCovers() => _coversWritten.Clear();

    private async Task<Track> BuildAsync(
        string title,
        string originalName,
        string codec,
        StoredFile stored,
        AudioMetadata metadata,
        AudioFormat format,
        CancellationToken ct)
    {
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

            if (album.CoverPath is null && metadata.CoverData is { Length: > 0 } coverData)
            {
                try
                {
                    using var source = new MemoryStream(coverData, writable: false);
                    var renditions = await imageProcessor.ToSquareWebpSetAsync(source, CoverVariants.Edges, ct);

                    album.CoverPath = await images.SaveCoverAsync(album.Id, renditions, ct);
                    _coversWritten.Add(album.CoverPath);

                    logger.LogDebug(
                        "Cover for album {AlbumId} re-encoded: {OriginalBytes} → {WebpBytes} bytes",
                        album.Id, coverData.Length, renditions.Sum(rendition => rendition.Content.Length));
                }
                catch (ValidationException ex)
                {
                    logger.LogWarning(
                        "Album {AlbumId} stays coverless: the embedded art could not be processed ({Reason})",
                        album.Id, ex.Message);
                }
            }
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
            OriginalFileName = originalName,
            MimeType = format.MimeType,
            FileSize = stored.SizeBytes,
            ContentHash = stored.ContentHash,
            CreatedAt = clock.GetUtcNow(),

            Codec = codec,
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
}
