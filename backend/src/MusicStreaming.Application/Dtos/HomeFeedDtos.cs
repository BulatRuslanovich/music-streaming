// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Dtos;

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

/// <summary>
/// Полоса главной, в которую попадает блок. Порядок значений — порядок сверху вниз.
/// </summary>
public enum HomeZone
{
    /// <summary>Одна крупная точка входа: микс дня и радиостанции.</summary>
    Lead,

    /// <summary>Ярлыки: ровно один ряд плиток.</summary>
    Quick,

    /// <summary>Всё остальное — секции с заголовками.</summary>
    Browse,
}

public record HomeBlockDto(
    string Key,
    string BaseKey,
    HomeBlockLayout Layout,
    HomeZone Zone,
    RecommendationReasonDto? Reason,
    IReadOnlyList<TrackDto>? Tracks,
    IReadOnlyList<AlbumDto>? Albums,
    IReadOnlyList<ArtistDto>? Artists,
    IReadOnlyList<PlaylistDto>? Playlists,
    int? TotalCount);


public enum HomeMixKind
{
    Daily,
    New,
    Top,
}

public record HomeMixDto(HomeMixKind Kind, IReadOnlyList<TrackDto> Tracks);

public record HomeFeedDto(
    IReadOnlyList<HomeBlockDto> Blocks,
    LibraryStatsDto Stats,
    bool IsColdStart,
    DateTimeOffset? GeneratedAt);
