// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Dtos;

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
