// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Dtos;

public record RecommendationReasonDto(string Kind, string? Subject, Guid? SubjectId);

/// <summary>
/// Почему трек оказался именно здесь в очереди. Заполняется только радио и DJ: в отличие от
/// <c>Score</c>, который остаётся отладкой для администратора, это пользовательский сигнал —
/// из него интерфейс делает пометку «звучит иначе» на треках дальней корзины.
/// </summary>
public record QueueSignalsDto(
    bool Explore,
    bool NewBoost,
    double CosineTaste,
    double CosineCurrent,
    int? ClusterId);

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

public enum DjMode
{
    Unknown = 0,
    ForYou = 1,
    Rediscover = 2,
    Discover = 3,
    Flow = 4,

    /// <summary>
    /// Непрослушанное у артистов, которых слушатель уже любит. Не предлагается в интерфейсе:
    /// режим находят, а не выбирают. От <see cref="Discover"/> отличается требованием знакомого
    /// артиста, от <see cref="Rediscover"/> — требованием, чтобы трек ещё не звучал.
    /// </summary>
    DeepCuts = 5,
}

public enum DjVariety
{
    Unknown = 0,
    Familiar = 1,
    Balanced = 2,
    Adventurous = 3,
}

public record DjRequest(
    DjMode Mode,
    DjVariety Variety,
    Guid? SeedTrackId,
    IReadOnlyList<Guid>? Exclude,
    int? Limit);

public record DjBatchDto(
    DjMode Mode,
    DjVariety Variety,
    Guid? SeedTrackId,
    IReadOnlyList<RecommendedTrackDto> Tracks);

public record RadioRequest(Guid? SeedTrackId, IReadOnlyList<Guid>? Exclude, int? Limit);

public record RadioBatchDto(IReadOnlyList<RecommendedTrackDto> Tracks, Guid? SeedTrackId)
{
    public static readonly RadioBatchDto Empty = new([], null);
}

public record RecommendationFeedbackRequest(SuppressionTarget Target, Guid TargetId);

public record RecommendationSuppressionDto(
    SuppressionTarget Target, Guid TargetId, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);
