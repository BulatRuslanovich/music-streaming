// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Dtos;

public record RecordEventsRequest(IReadOnlyList<PlaybackEventRequest>? Events);

public record PlaybackEventRequest(
    string? Type,
    Guid? TrackId,
    Guid? EntityId,
    DateTimeOffset? OccurredAt,
    int? PositionSeconds,
    int? ListenedSeconds,
    int? DurationSeconds,
    Guid? SessionId,
    string? Source,
    string? SourceId,
    string? Platform);
