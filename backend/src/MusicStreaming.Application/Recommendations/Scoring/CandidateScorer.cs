// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Options;

namespace MusicStreaming.Application.Recommendations.Scoring;

public record RankingContext(
    IReadOnlyDictionary<Guid, double> ArtistScores,
    IReadOnlyDictionary<Guid, double> GenreScores,
    IReadOnlyDictionary<Guid, TrackHistory> History,
    IReadOnlyDictionary<Guid, DateTimeOffset> LastShown,
    DateTimeOffset Now)
{
    public static RankingContext Empty(DateTimeOffset now) =>
        new(new Dictionary<Guid, double>(), new Dictionary<Guid, double>(),
            new Dictionary<Guid, TrackHistory>(), new Dictionary<Guid, DateTimeOffset>(), now);
}

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
        RankingWeights weights,
        RecommendationOptions options)
    {
        candidate.Behavior = BehaviorScore(candidate, context);

        var merit = weights.Combine(
            candidate.Content,
            candidate.Collaborative,
            candidate.Behavior,
            candidate.Popularity,
            candidate.Freshness,
            candidate.Coverage);

        var confirmations = Math.Clamp(candidate.EvidenceCount - 1, 0, 3);
        var consensus = 1 + confirmations * options.MultiSourceBonus;

        candidate.Score = merit * consensus * PenaltyFor(candidate, context, options);
    }

    public static double BehaviorScore(RecommendationCandidate candidate, RankingContext context)
    {
        var artist = 0.0;
        foreach (var artistId in candidate.ArtistIds.Count > 0 ? candidate.ArtistIds : [candidate.ArtistId])
        {
            if (context.ArtistScores.TryGetValue(artistId, out var score))
                artist = Math.Abs(score) > Math.Abs(artist) ? score : artist;
        }

        var genre = candidate.GenreId is { } genreId && context.GenreScores.TryGetValue(genreId, out var g)
            ? g
            : 0;

        return Math.Clamp(artist * 0.7 + genre * 0.3, -1, 1);
    }

    public static double PenaltyFor(
        RecommendationCandidate candidate,
        RankingContext context,
        RecommendationOptions options)
    {
        var penalty = 1.0;

        if (context.History.TryGetValue(candidate.TrackId, out var history))
        {
            var sinceLastPlay = context.Now - history.LastPlayedAt;

            if (sinceLastPlay < TimeSpan.FromHours(options.JustPlayedHours))
                penalty *= options.JustPlayedPenalty;
            else if (sinceLastPlay < TimeSpan.FromDays(options.RecentlyPlayedDays))
                penalty *= options.RecentlyPlayedPenalty;

            if (history is { SkipCount: >= 2, AverageCompletion: < 0.2 })
                penalty *= options.DislikedTrackPenalty;
        }

        if (context.LastShown.TryGetValue(candidate.TrackId, out var shownAt)
            && context.Now - shownAt < TimeSpan.FromDays(options.ImpressionCooldownDays))
        {
            penalty *= options.UnclickedImpressionPenalty;
        }

        if (candidate.Behavior < -0.3)
            penalty *= options.DislikedArtistPenalty;

        return penalty;
    }
}
