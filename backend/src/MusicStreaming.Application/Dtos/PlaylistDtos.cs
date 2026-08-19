// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Dtos;

public record PlaylistDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsPublic,
    Guid OwnerId,
    string OwnerName,
    int TrackCount,
    int DurationSeconds,
    bool HasCover,
    Guid? CoverTrackId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record PlaylistDetailDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsPublic,
    Guid OwnerId,
    string OwnerName,
    int DurationSeconds,
    bool HasCover,
    Guid? CoverTrackId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<TrackDto> Tracks);

public record CreatePlaylistRequest(string Name, string? Description, bool IsPublic = false);
public record UpdatePlaylistRequest(string Name, string? Description, bool IsPublic = false);
public record AddPlaylistTrackRequest(Guid TrackId);
public record ReorderPlaylistRequest(IReadOnlyList<Guid> TrackIds);
