// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Common;

namespace MusicStreaming.Application.Dtos;


public record ArtistRefDto(Guid Id, string Name);

public record TrackDto(
    Guid Id,
    string Title,
    Guid ArtistId,
    string ArtistName,
    IReadOnlyList<ArtistRefDto> Artists,
    Guid? AlbumId,
    string? AlbumTitle,
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

public record GenreDto(
    Guid Id,
    string Name,
    int TrackCount,
    IReadOnlyList<Guid> CoverAlbumIds);

public enum SearchResultKind { Artist, Album, Track, Genre }

public record SearchTopResultDto(
    SearchResultKind Kind,
    ArtistDto? Artist,
    AlbumDto? Album,
    TrackDto? Track,
    GenreDto? Genre);

public record SearchResultDto(
    IReadOnlyList<ArtistDto> Artists,
    IReadOnlyList<AlbumDto> Albums,
    IReadOnlyList<TrackDto> Tracks,
    IReadOnlyList<GenreDto> Genres,
    SearchTopResultDto? Top);

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
    long TotalDurationSeconds,
    long TotalBytes,
    int FavoriteCount);

public record LibraryOverviewDto(
    LibraryStatsDto Stats,
    IReadOnlyList<TrackDto> RecentTracks,
    IReadOnlyList<AlbumDto> RecentAlbums,
    IReadOnlyList<ArtistDto> RecentArtists,
    IReadOnlyList<GenreDto> TopGenres);


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

public record BulkDeleteResultDto(int Deleted, IReadOnlyList<Guid> Missing);

public record UploadProbeFileDto(
    string FileName,
    string? ContentHash,
    string? Title,
    string? Artist);

public record UploadProbeRequest(IReadOnlyList<UploadProbeFileDto> Files);

public enum UploadProbeVerdict
{
    New,
    Duplicate,
    Similar,
}


public enum UploadProbeBasis
{
    None,
    Tags,
    Hash,
    HashAndTags,
}

public record UploadProbeMatchDto(
    string FileName,
    UploadProbeVerdict Verdict,
    UploadProbeBasis Basis,
    TrackDto? Match);

public record UploadProbeResultDto(IReadOnlyList<UploadProbeMatchDto> Files);

public record UpdateArtistRequest(string Name);

public record UpdateAlbumRequest(
    string? Title,
    string? Artist,
    int? Year);

public record LibraryImportStatusDto(
    bool Enabled,
    string Directory,
    bool Running,
    int Waiting,
    int Pending,
    int Imported,
    int Failed,
    string? CurrentFile,
    IReadOnlyList<UploadFailureDto> RecentFailures);
