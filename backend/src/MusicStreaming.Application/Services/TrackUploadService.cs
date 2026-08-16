using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Options;
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
    IOptions<StorageOptions> storageOptions,
    ILogger<TrackUploadService> logger)
{
    private static readonly string[] AllowedContentTypes =
        ["audio/mpeg", "audio/mp3", "audio/mpeg3", "audio/x-mpeg-3", "application/octet-stream"];

    private long MaxUploadBytes => storageOptions.Value.MaxUploadBytes;

    public async Task<UploadResultDto> UploadAsync(
        IReadOnlyList<UploadCandidate> files,
        CancellationToken ct = default)
    {
        if (files.Count == 0)
            throw new ValidationException("No files were provided.");

        var uploaded = new List<TrackDto>();
        var failed = new List<UploadFailureDto>();

        foreach (var file in files)
        {
            try
            {
                uploaded.Add(await UploadSingleAsync(file, ct));
            }
            catch (AppException ex)
            {
                DiscardPending();
                logger.LogWarning("Upload of {FileName} rejected: {Reason}", file.FileName, ex.Message);
                failed.Add(new UploadFailureDto(file.FileName, ex.Message));
            }
            catch (Exception ex)
            {
                DiscardPending();
                logger.LogError(ex, "Unexpected failure while uploading {FileName}", file.FileName);
                failed.Add(new UploadFailureDto(file.FileName, "The file could not be processed."));
            }
        }

        return new UploadResultDto(uploaded, failed);
    }

    /// <summary>
    /// Забывает всё, что не успел записать отвергнутый файл.
    ///
    /// <para>
    /// Каждый файл сохраняется одним разом в конце, поэтому незаписанным остаётся ровно то, что
    /// завёл упавший. Без этого его исполнитель, альбом и сам трек уехали бы в базу вместе со
    /// следующим файлом — причём трек ссылался бы на аудиофайл, который к тому моменту уже удалён.
    /// </para>
    ///
    /// <para>
    /// Отслеживание сбрасывается целиком, а не по одной записи: отцепить исполнителя, пока рядом
    /// висит его альбом, нельзя — EF считает разорванной обязательную связь и бросает исключение
    /// прямо посреди уборки. Уцелевшее от прошлых файлов уже сохранено, поэтому терять сброс
    /// нечему: следующий файл просто перечитает нужное.
    /// </para>
    /// </summary>
    private void DiscardPending()
    {
        db.ChangeTracker.Clear();

        // Запомненное резолвером указывает на те же — теперь отцепленные — сущности.
        tags.Forget();
    }

    private async Task<TrackDto> UploadSingleAsync(UploadCandidate file, CancellationToken ct)
    {
        ValidateEnvelope(file);

        StoredFile stored;
        await using (var input = file.OpenReadStream())
        {
            stored = await storage.SaveTrackAsync(input, MaxUploadBytes, ct);
        }

        try
        {
            if (stored.SizeBytes == 0)
                throw new ValidationException("The file is empty.");

            var duplicate = await db.Tracks
                .Where(t => t.ContentHash == stored.ContentHash)
                .Select(t => t.Id)
                .FirstOrDefaultAsync(ct);

            if (duplicate != Guid.Empty)
                throw new ConflictException("This file is already in the library.");

            var absolutePath = storage.ResolveExisting(stored.RelativePath)
                ?? throw new ValidationException("The uploaded file could not be read back.");

            var metadata = metadataReader.Read(absolutePath)
                ?? throw new ValidationException("The file is not a readable MP3.");

            if (metadata.DurationSeconds <= 0)
                throw new ValidationException("The file contains no audio stream.");

            var track = await BuildTrackAsync(file, stored, metadata, ct);
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Uploaded track {TrackId} ({Title}) from {FileName}, {Bytes} bytes",
                track.Id, track.Title, file.FileName, stored.SizeBytes);

            return await catalog.GetTrackAsync(track.Id, ct);
        }
        catch
        {
            storage.Delete(stored.RelativePath);
            throw;
        }
    }

    private void ValidateEnvelope(UploadCandidate file)
    {
        var extension = Path.GetExtension(file.FileName);
        if (!string.Equals(extension, ".mp3", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Only .mp3 files are supported.");

        if (file.ContentType is not null &&
            !AllowedContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            throw new ValidationException($"Unsupported content type '{file.ContentType}'.");
        }

        if (file.Length > MaxUploadBytes)
            throw new ValidationException($"The file exceeds the {MaxUploadBytes / (1024 * 1024)} MB limit.");
    }

    private async Task<Track> BuildTrackAsync(
        UploadCandidate file, StoredFile stored, AudioMetadata metadata, CancellationToken ct)
    {
        var title = Text.TrimToNull(metadata.Title) ?? Path.GetFileNameWithoutExtension(file.FileName);

        // Ни одного промежуточного сохранения: идентификаторы у сущностей клиентские
        // (<c>Guid.CreateVersion7</c>), а повторно названного исполнителя или альбом узнаёт по
        // памяти сам <see cref="TagResolver"/>. Всё, что здесь заведено, уходит в базу одним
        // сохранением в конце — вместе с самим треком.
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
            Year = metadata.Year ?? album?.Year,
            DurationSeconds = metadata.DurationSeconds,
            FilePath = stored.RelativePath,
            OriginalFileName = SafeOriginalName(file.FileName),
            MimeType = "audio/mpeg",
            FileSize = stored.SizeBytes,
            ContentHash = stored.ContentHash,
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
