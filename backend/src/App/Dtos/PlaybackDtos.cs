// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Dtos;

public record PlaybackStateReport(
    string? DeviceId,
    string? DeviceName,
    IReadOnlyList<Guid>? TrackIds,
    int Index,
    double PositionSeconds,
    bool IsPlaying,
    bool Shuffle,
    string? Repeat);

public record PlayingElsewhereDto(
    string DeviceId,
    string DeviceName,
    TrackDto Track,
    double PositionSeconds,
    bool IsPlaying,
    DateTimeOffset ReportedAt);

public record PlaybackTakeoverDto(string DeviceId, string DeviceName);

public record PlaybackHandoffRequest(string? DeviceId);

public record PlaybackHandoffDto(
    string DeviceId,
    string DeviceName,
    IReadOnlyList<TrackDto> Tracks,
    int Index,
    double PositionSeconds,
    bool Shuffle,
    string Repeat);
