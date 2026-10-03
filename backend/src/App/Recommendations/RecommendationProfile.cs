// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Domain.Entities.Recommendations;

namespace App.Recommendations;

public readonly record struct RecommendationSeed(Guid TrackId, double Weight);

public record UserRecommendationContext(Guid UserId, UserTasteProfile Profile, RankingContext Ranking);

public record RankingContext(
    IReadOnlyDictionary<Guid, double> ArtistScores,
    IReadOnlyDictionary<Guid, double> GenreScores,
    IReadOnlyDictionary<Guid, TrackHistory> History,
    DateTimeOffset Now,
    double? YearCenter = null,
    double YearSpread = 0);

public record TrackHistory(
    DateTimeOffset LastPlayedAt,
    int PlayCount,
    int SkipCount,
    double AverageCompletion,
    double Score,
    int CompletedCount = 0,
    int ReplayCount = 0,
    int PlaylistAdds = 0);
