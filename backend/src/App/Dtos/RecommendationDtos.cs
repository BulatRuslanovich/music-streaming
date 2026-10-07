// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Dtos;

public record RecommendationReasonDto(string Kind, string? Subject, Guid? SubjectId);

public record QueueSignalsDto(bool Explore);

public record RecommendedTrackDto(
    TrackDto Track,
    RecommendationReasonDto Reason,
    double? Score,
    QueueSignalsDto? Signals = null);

public record RecommendationSectionDto(
    string Key,
    string BaseKey,
    RecommendationReasonDto? Reason,
    IReadOnlyList<RecommendedTrackDto>? Tracks,
    IReadOnlyList<ArtistDto>? Artists,
    IReadOnlyList<AlbumDto>? Albums);

public record RadioRequest(Guid? SeedTrackId, IReadOnlyList<Guid>? Exclude, int? Limit, string? Mood = null);

public record MoodDto(string Key);

public record RadioBatchDto(IReadOnlyList<RecommendedTrackDto> Tracks, Guid? SeedTrackId);


