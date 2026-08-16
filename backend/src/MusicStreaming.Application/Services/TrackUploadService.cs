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
    TranscodeQueue transcodeQueue,
    IOptions<StorageOptions> storageOptions,
    ILogger<TrackUploadService> logger)
{
    /// <summary>
    /// Сколько раз файл переспрашивает про исполнителя, альбом и жанр, проиграв гонку за них.
    ///
    /// <para>
    /// Одного повтора хватает почти всегда: победитель к этому моменту уже записан, и второй заход
    /// находит его строку. Запас нужен на случай, когда одновременно приехало больше двух файлов
    /// одного альбома и разойтись им приходится в несколько шагов.
    /// </para>
    /// </summary>
    private const int TagConflictAttempts = 4;

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
        var format = ValidateEnvelope(file);

        StoredFile stored;
        await using (var input = file.OpenReadStream())
        {
            stored = await storage.SaveTrackAsync(input, format.Extension, MaxUploadBytes, ct);
        }

        try
        {
            if (stored.SizeBytes == 0)
                throw new ValidationException("The file is empty.");

            var absolutePath = storage.ResolveExisting(stored.RelativePath)
                ?? throw new ValidationException("The uploaded file could not be read back.");

            if (AudioUpload.SniffContainer(absolutePath) is { } actual && actual != format.Extension)
                throw new ValidationException($"The file is not a {format.Label} file despite its name.");

            var metadata = metadataReader.Read(absolutePath, format.TagLibMimeType)
                ?? throw new ValidationException($"The file is not a readable {format.Label} file.");

            if (metadata.DurationSeconds <= 0)
                throw new ValidationException("The file contains no audio stream.");

            var track = await SaveTrackAsync(file, stored, metadata, format, ct);

            logger.LogInformation(
                "Uploaded track {TrackId} ({Title}) from {FileName}, {Codec}, {Bytes} bytes",
                track.Id, track.Title, file.FileName, track.Codec, stored.SizeBytes);

            PrepareUnplayableOriginal(track);

            return await catalog.GetTrackAsync(track.Id, ct);
        }
        catch
        {
            storage.Delete(stored.RelativePath);
            throw;
        }
    }

    /// <summary>
    /// Всё, что можно узнать о файле, не читая его: расширение и размер.
    ///
    /// <para>
    /// Заявленный браузером тип содержимого не проверяется. Он подделывается тривиально, а для
    /// одного и того же файла разные браузеры присылают то <c>audio/x-flac</c>, то <c>video/mp4</c>,
    /// то пустую строку — отказ по нему был бы ошибкой, видимой человеку, и ничем не защищал бы.
    /// Настоящий фильтр — разбор TagLib, и он ждёт файл дальше по пути.
    /// </para>
    /// </summary>
    private AudioFormat ValidateEnvelope(UploadCandidate file)
    {
        var format = AudioUpload.For(file.FileName)
            ?? throw new ValidationException($"Only {AudioUpload.Accepted} files are supported.");

        if (file.Length > MaxUploadBytes)
            throw new ValidationException($"The file exceeds the {MaxUploadBytes / (1024 * 1024)} MB limit.");

        return format;
    }

    /// <summary>
    /// Заводит трек, переживая гонку за общими сущностями.
    ///
    /// <para>
    /// Исполнитель, альбом и жанр — общие на всю библиотеку, а память <see cref="TagResolver"/>
    /// живёт ровно один запрос. Два файла одного альбома, приехавшие одновременно, оба не находят
    /// исполнителя, оба его заводят — и второй упирается в уникальный индекс по
    /// <c>normalized_name</c>. Проигравшему достаточно переспросить: строки, которой он не нашёл,
    /// теперь есть, и повтор подберёт чужую вместо того, чтобы заводить свою.
    /// </para>
    ///
    /// <para>
    /// В повтор входит и проверка на дубликат: одновременно приехавшие одинаковые файлы оба её
    /// проходят, и проигравшему честнее ответить «уже в библиотеке», чем «не удалось обработать».
    /// </para>
    /// </summary>
    private async Task<Track> SaveTrackAsync(
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

            try
            {
                await db.SaveChangesAsync(ct);
                return track;
            }
            catch (DbUpdateException) when (attempt < TagConflictAttempts)
            {
                // Незаписанное отцепляется целиком — вместе с памятью резолвера, которая на него
                // ссылается: вернуть запомненное после отмены значило бы сослаться на строку,
                // которой никогда не будет.
                DiscardPending();

                logger.LogDebug(
                    "Retrying {FileName} after losing a race for its artist, album or genre (attempt {Attempt})",
                    file.FileName, attempt);
            }
        }
    }

    /// <summary>
    /// Ставит перекодирование в очередь заранее для того, что браузер не возьмётся играть сам.
    ///
    /// <para>
    /// ALAC не понимают ни Chrome, ни Firefox, а плеер, упёршийся в такой исходник, попросит
    /// экономную ступень — и получит в ответ снова исходник, потому что ступени ещё нет. Ждать
    /// ffmpeg внутри запроса он не умеет. Значит, ступень должна появиться до первого включения, а
    /// не по нему.
    /// </para>
    /// </summary>
    private void PrepareUnplayableOriginal(Track track)
    {
        if (track.Codec is not "alac")
            return;

        transcodeQueue.TryEnqueue(new TranscodeRequest(track.ContentHash, track.FilePath, AudioQuality.Normal));
    }

    private async Task<Track> BuildTrackAsync(
        UploadCandidate file, StoredFile stored, AudioMetadata metadata, AudioFormat format, CancellationToken ct)
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
            MimeType = format.MimeType,
            FileSize = stored.SizeBytes,
            ContentHash = stored.ContentHash,

            // Кодек берётся из самого потока, а расширение остаётся запасным вариантом: у .m4a оно
            // не различает ALAC и AAC, но для остальных форматов совпадает с кодеком.
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
