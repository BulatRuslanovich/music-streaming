// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace Domain.Entities.Recommendations;

public record CachedRecommendation(
    Guid ItemId,
    RecommendedItemKind Kind,
    double Score,
    string ReasonKind,
    string? ReasonSubject,
    Guid? ReasonSubjectId);

public class RecommendationCacheEntry
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string ShelfKey { get; set; } = string.Empty;
    public int Position { get; set; }
    public IReadOnlyList<CachedRecommendation> Payload { get; set; } = [];
    public DateTimeOffset ExpiresAt { get; set; }
}
