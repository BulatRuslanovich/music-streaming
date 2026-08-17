using MusicStreaming.Application.Common;

namespace MusicStreaming.Application.Dtos;


public record ArtistRefDto(Guid Id, string Name);

/// <param name="Artists">Все титры трека по порядку; первый совпадает с <paramref name="ArtistId"/>.</param>
/// <param name="HasLyrics">Есть ли у трека текст. Едет вместе с треком, чтобы плеер не спрашивал про каждый трек отдельно только ради того, показывать ли кнопку.</param>
/// <param name="Codec">
/// Кодек исходника; <c>null</c> у треков, залитых до того, как это стали записывать. Плееру он
/// нужен не для подписи, а чтобы заранее спросить у браузера, возьмётся ли тот за оригинал.
/// </param>
public record TrackDto(
    Guid Id,
    string Title,
    Guid ArtistId,
    string ArtistName,
    IReadOnlyList<ArtistRefDto> Artists,
    Guid? AlbumId,
    string? AlbumTitle,
    Guid? GenreId,
    string? GenreName,
    int? TrackNumber,
    int? DiscNumber,
    int? Year,
    int DurationSeconds,
    string OriginalFileName,
    bool IsFavorite,
    bool HasCover,
    bool HasLyrics,
    DateTimeOffset CreatedAt,
    string? Codec,
    int? BitrateKbps,
    int? SampleRateHz,
    int? BitsPerSample);

public record ArtistDto(
    Guid Id,
    string Name,
    int AlbumCount,
    int TrackCount,
    bool HasImage);

public record ArtistDetailDto(
    Guid Id,
    string Name,
    bool HasImage,
    IReadOnlyList<AlbumDto> Albums,
    PagedResult<TrackDto> Tracks);

public record AlbumDto(
    Guid Id,
    string Title,
    Guid ArtistId,
    string ArtistName,
    int? Year,
    int TrackCount,
    int DurationSeconds,
    bool HasCover,
    DateTimeOffset CreatedAt);

public record AlbumDetailDto(
    Guid Id,
    string Title,
    Guid ArtistId,
    string ArtistName,
    int? Year,
    bool HasCover,
    int DurationSeconds,
    IReadOnlyList<TrackDto> Tracks);

public record GenreDto(Guid Id, string Name, int TrackCount);

public record SearchResultDto(
    IReadOnlyList<ArtistDto> Artists,
    IReadOnlyList<AlbumDto> Albums,
    IReadOnlyList<TrackDto> Tracks,
    IReadOnlyList<GenreDto> Genres);

public record HistoryEntryDto(
    Guid Id,
    TrackDto Track,
    DateTimeOffset PlayedAt,
    int PlaybackPosition);

public record HomeSummaryDto(
    IReadOnlyList<TrackDto> RecentlyAdded,
    IReadOnlyList<TrackDto> RecentlyPlayed,
    IReadOnlyList<TrackDto> Favorites,
    IReadOnlyList<AlbumDto> Albums,
    IReadOnlyList<PlaylistDto> Playlists,
    LibraryStatsDto Stats);

public record LibraryStatsDto(
    int TrackCount,
    int AlbumCount,
    int ArtistCount,
    int PlaylistCount,
    long TotalDurationSeconds,
    long TotalBytes);


public record UpdateTrackRequest(
    string? Title,
    string? Artist,
    string? Album,
    string? Genre,
    int? Year,
    int? TrackNumber,
    int? DiscNumber);

public record UploadResultDto(
    IReadOnlyList<TrackDto> Uploaded,
    IReadOnlyList<UploadFailureDto> Failed);

public record UploadFailureDto(string FileName, string Reason);

public record BulkDeleteTracksRequest(IReadOnlyList<Guid>? Ids);

/// <summary>
/// Итог пакетного удаления.
///
/// <para>
/// Причин по каждому идентификатору здесь намеренно нет — в отличие от загрузки, где у каждого
/// файла своя история отказа. Удаление уходит одним оператором: строка либо была, либо нет,
/// третьего исхода не существует.
/// </para>
/// </summary>
/// <param name="Deleted">Сколько строк действительно ушло.</param>
/// <param name="Missing">Названное, чего в библиотеке уже не было.</param>
public record BulkDeleteResultDto(int Deleted, IReadOnlyList<Guid> Missing);

/// <summary>
/// Что известно о файле до того, как он пересёк сеть: хеш содержимого и теги, вычитанные в браузере.
/// Любое поле может отсутствовать — старый браузер не отдаст хеш, файл без ID3 не отдаст теги.
/// </summary>
public record UploadProbeFileDto(
    string FileName,
    string? ContentHash,
    string? Title,
    string? Artist);

public record UploadProbeRequest(IReadOnlyList<UploadProbeFileDto> Files);

public enum UploadProbeVerdict
{
    /// <summary>Ничего похожего в библиотеке нет — файл нужно загружать.</summary>
    New,

    /// <summary>Ровно этот файл уже лежит в библиотеке: хеши совпали побайтово.</summary>
    Duplicate,

    /// <summary>Похоже, эта песня уже есть, но другим файлом: совпали исполнитель и название.</summary>
    Similar,
}

/// <summary>Ответ идёт в том же порядке, что и запрос, — файлы сопоставляются по позиции.</summary>
public record UploadProbeMatchDto(string FileName, UploadProbeVerdict Verdict, TrackDto? Match);

public record UploadProbeResultDto(IReadOnlyList<UploadProbeMatchDto> Files);

public record UpdateArtistRequest(string Name);
