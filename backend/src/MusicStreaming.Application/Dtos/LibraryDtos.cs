namespace MusicStreaming.Application.Dtos;

/// <summary>An artist as it is referenced from a track credit: enough to render a link.</summary>
public sealed record ArtistRefDto(Guid Id, string Name);

public sealed record TrackDto(
    Guid Id,
    string Title,
    Guid ArtistId,
    string ArtistName,
    // Every credited artist in tag order; the first is the primary one above.
    IReadOnlyList<ArtistRefDto> Artists,
    Guid? AlbumId,
    string? AlbumTitle,
    Guid? GenreId,
    string? GenreName,
    int? TrackNumber,
    int? DiscNumber,
    int? Year,
    int DurationSeconds,
    long FileSize,
    string OriginalFileName,
    bool IsFavorite,
    bool HasCover,
    DateTimeOffset CreatedAt);

public sealed record ArtistDto(
    Guid Id,
    string Name,
    int AlbumCount,
    int TrackCount);

public sealed record ArtistDetailDto(
    Guid Id,
    string Name,
    IReadOnlyList<AlbumDto> Albums,
    IReadOnlyList<TrackDto> Tracks);

public sealed record AlbumDto(
    Guid Id,
    string Title,
    Guid ArtistId,
    string ArtistName,
    int? Year,
    int TrackCount,
    int DurationSeconds,
    bool HasCover,
    DateTimeOffset CreatedAt);

public sealed record AlbumDetailDto(
    Guid Id,
    string Title,
    Guid ArtistId,
    string ArtistName,
    int? Year,
    bool HasCover,
    int DurationSeconds,
    IReadOnlyList<TrackDto> Tracks);

public sealed record GenreDto(Guid Id, string Name, int TrackCount);

public sealed record SearchResultDto(
    IReadOnlyList<ArtistDto> Artists,
    IReadOnlyList<AlbumDto> Albums,
    IReadOnlyList<TrackDto> Tracks,
    IReadOnlyList<GenreDto> Genres);

public sealed record HistoryEntryDto(
    Guid Id,
    TrackDto Track,
    DateTimeOffset PlayedAt,
    int PlaybackPosition);

public sealed record HomeSummaryDto(
    IReadOnlyList<TrackDto> RecentlyAdded,
    IReadOnlyList<TrackDto> RecentlyPlayed,
    IReadOnlyList<TrackDto> Favorites,
    IReadOnlyList<AlbumDto> Albums,
    IReadOnlyList<PlaylistDto> Playlists,
    LibraryStatsDto Stats);

public sealed record LibraryStatsDto(
    int TrackCount,
    int AlbumCount,
    int ArtistCount,
    int PlaylistCount,
    long TotalDurationSeconds,
    long TotalBytes);

/// <summary>Fields a user may correct when ID3 tags were missing or wrong.</summary>
public sealed record UpdateTrackRequest(
    string? Title,
    string? Artist,
    string? Album,
    string? Genre,
    int? Year,
    int? TrackNumber,
    int? DiscNumber);

public sealed record UploadResultDto(
    IReadOnlyList<TrackDto> Uploaded,
    IReadOnlyList<UploadFailureDto> Failed);

public sealed record UploadFailureDto(string FileName, string Reason);
