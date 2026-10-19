// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Dtos;

public record MapPointDto(
    Guid Id,
    float X,
    float Y,
    string Title,
    string Artist,
    Guid? AlbumId,
    bool HasCover,
    string? Genre,
    int Plays);

// Где на карте лежит настроение: середина самых подходящих ему треков.
public record MapLandmarkDto(string Mood, float X, float Y);

public record LibraryMapDto(
    IReadOnlyList<MapPointDto> Points,
    IReadOnlyList<MapLandmarkDto> Landmarks,
    int TotalTracks);

public record TrackLookupRequest(IReadOnlyList<Guid>? Ids);
