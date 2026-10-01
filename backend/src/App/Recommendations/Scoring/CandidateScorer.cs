// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using static App.Recommendations.RecommendationTuning;

namespace App.Recommendations.Scoring;

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

public static class CandidateScorer
{
    public static void Score(
        RecommendationCandidate candidate,
        RankingContext context,
        RankingWeights weights)
    {
        candidate.Behavior = BehaviorScore(candidate, context);

        var merit = weights.Combine(
            candidate.Content,
            candidate.TasteFit,
            candidate.AudioSimilarity,
            candidate.Collaborative,
            candidate.Behavior,
            candidate.Popularity,
            candidate.Freshness,
            candidate.Coverage);

        var confirmations = Math.Clamp(candidate.EvidenceCount - 1, 0, 3);
        var consensus = 1 + confirmations * RecommendationTuning.Penalties.MultiSourceBonus;

        candidate.Score = merit * consensus * PenaltyFor(candidate, context);
    }

    public static double BehaviorScore(RecommendationCandidate candidate, RankingContext context)
    {
        var total = 0.0;
        var weight = 0.0;

        foreach (var artistId in candidate.ArtistIds.Count > 0 ? candidate.ArtistIds : [candidate.ArtistId])
        {
            if (!context.ArtistScores.TryGetValue(artistId, out var score))
                continue;

            var share = artistId == candidate.ArtistId ? 1.0 : 0.5;

            if (score < 0 && artistId != candidate.ArtistId)
                share *= 0.5;

            total += score * share;
            weight += share;
        }

        var artist = weight > 0 ? total / weight : 0;

        var genre = candidate.GenreId is { } genreId && context.GenreScores.TryGetValue(genreId, out var g)
            ? g
            : 0;

        return Math.Clamp(artist * 0.7 + genre * 0.3, -1, 1);
    }

    public static double PenaltyFor(
        RecommendationCandidate candidate,
        RankingContext context)
    {
        var penalty = 1.0;

        if (context.History.TryGetValue(candidate.TrackId, out var history))
        {
            var sinceLastPlay = context.Now - history.LastPlayedAt;

            if (sinceLastPlay < TimeSpan.FromHours(RecommendationTuning.Penalties.JustPlayedHours))
                penalty *= RecommendationTuning.Penalties.JustPlayed;
            else if (sinceLastPlay < TimeSpan.FromDays(RecommendationTuning.Penalties.RecentlyPlayedDays))
                penalty *= RecommendationTuning.Penalties.RecentlyPlayed;

            if (history is { SkipCount: >= 2, AverageCompletion: < 0.2 })
                penalty *= RecommendationTuning.Penalties.DislikedTrack;
        }

        if (candidate.Behavior < -0.3)
            penalty *= RecommendationTuning.Penalties.DislikedArtist;

        penalty *= QualityFactor(candidate);
        penalty *= EraFactor(candidate, context);

        return penalty;
    }

    public static double QualityFactor(RecommendationCandidate candidate)
    {
        if (candidate.GlobalSkipRate is not { } skipRate)
            return 1;

        var threshold = RecommendationTuning.Penalties.HighSkipRateThreshold;
        if (skipRate <= threshold)
            return 1;

        var excess = Math.Clamp((skipRate - threshold) / (1 - threshold), 0, 1);

        return 1 - (1 - RecommendationTuning.Penalties.HighSkipRatePenalty) * excess;
    }

    public static double EraFactor(
        RecommendationCandidate candidate, RankingContext context)
    {
        if (context.YearCenter is not { } center || candidate.Year is not { } year)
            return 1;

        var spread = Math.Max(context.YearSpread, RecommendationTuning.Penalties.MinimumYearSpread);
        var distance = (year - center) / spread;
        var fit = Math.Exp(-0.5 * distance * distance);

        return RecommendationTuning.Penalties.EraFitFloor + (1 - RecommendationTuning.Penalties.EraFitFloor) * fit;
    }
}
