// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Dtos;

public record RecommendationReasonDto(string Kind, string? Subject, Guid? SubjectId);

/// <summary>
/// Почему трек оказался именно здесь в очереди. Заполняется только радио: в отличие от
/// <c>Score</c>, который остаётся отладкой для администратора, это пользовательский сигнал —
/// из него интерфейс делает пометку «звучит иначе» на треках дальней корзины.
/// </summary>
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

public record RecommendationHomeDto(
    IReadOnlyList<RecommendationSectionDto> Sections,
    bool IsColdStart);

public record RadioRequest(Guid? SeedTrackId, IReadOnlyList<Guid>? Exclude, int? Limit);

public record RadioBatchDto(IReadOnlyList<RecommendedTrackDto> Tracks, Guid? SeedTrackId);

public record RecommendationFeedbackRequest(SuppressionTarget Target, Guid TargetId);

public record RecommendationSuppressionDto(
    SuppressionTarget Target, Guid TargetId, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);
