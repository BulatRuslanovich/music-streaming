// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Dtos;

public enum HomeBlockLayout
{
    Shelf,

    Hero,

    Tile,

    QuickTiles,

    Grid,

    Chart,

    Circles,
}

public enum HomeZone
{
    Lead,

    Quick,

    Browse,
}

public record HomeBlockDto(
    string Key,
    string BaseKey,
    HomeBlockLayout Layout,
    HomeZone Zone,
    RecommendationReasonDto? Reason = null,
    IReadOnlyList<TrackDto>? Tracks = null,
    IReadOnlyList<AlbumDto>? Albums = null,
    IReadOnlyList<ArtistDto>? Artists = null,
    IReadOnlyList<PlaylistDto>? Playlists = null,
    int? TotalCount = null);

public enum HomeMixKind
{
    Daily,
    New,
    Top,
}

public record HomeMixDto(HomeMixKind Kind, IReadOnlyList<TrackDto> Tracks);

public record HomeFeedDto(
    IReadOnlyList<HomeBlockDto> Blocks,
    LibraryStatsDto Stats);
