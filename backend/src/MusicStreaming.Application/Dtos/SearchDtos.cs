// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Dtos;

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
