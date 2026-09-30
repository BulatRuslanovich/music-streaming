// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Recommendations.Scoring;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Recommendations;

public readonly record struct RecommendationSeed(Guid TrackId, double Weight);

public record UserRecommendationContext(
    Guid UserId,
    UserTasteProfile Profile,
    RankingContext Ranking,
    IReadOnlyList<RecommendationSeed> Seeds,
    IReadOnlyDictionary<Guid, double> GenreShare)
{
    public bool IsColdStart => Profile.PositiveSignalCount == 0;
    public IReadOnlyList<Guid> SeedTrackIds => Seeds.Select(seed => seed.TrackId).ToList();
}
