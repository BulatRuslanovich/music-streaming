// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Application.Recommendations.Scoring;

namespace MusicStreaming.Application.Services.Recommendations;

internal static class DjSelectionPolicy
{
    public static double ExplorationRatio(DjVariety variety) => variety switch
    {
        DjVariety.Familiar => 0.10,
        DjVariety.Balanced => 0.35,
        DjVariety.Adventurous => 0.70,
        _ => throw new ValidationException("Unknown DJ variety."),
    };

    public static void Score(
        RecommendationCandidate candidate,
        RankingContext context,
        RankingWeights personalWeights,
        RecommendationOptions options,
        DjMode mode)
    {
        var weights = mode switch
        {
            DjMode.Flow => RankingWeights.FlowDefaults(),
            DjMode.Discover => RankingWeights.DiscoverDefaults(),
            _ => personalWeights,
        };

        CandidateScorer.Score(candidate, context, weights, options);

        if (candidate.AudioSimilarity is { } audio && mode is DjMode.Flow or DjMode.Discover)
        {
            var penalty = CandidateScorer.PenaltyFor(candidate, context, options);
            var audioWeight = mode == DjMode.Flow ? 0.30 : 0.15;
            var baseMerit = candidate.Score / Math.Max(penalty, double.Epsilon);
            candidate.Score = ((1 - audioWeight) * baseMerit + audioWeight * audio) * penalty;
        }

        if (mode == DjMode.Rediscover && context.History.TryGetValue(candidate.TrackId, out var history))
        {
            var completion = Math.Clamp(history.AverageCompletion, 0, 1);
            var repetition = 1 - Math.Exp(-Math.Max(1, history.PlayCount) / 3.0);
            var relationship = 0.55 * Math.Max(0, history.Score) + 0.30 * completion + 0.15 * repetition;
            var penalty = CandidateScorer.PenaltyFor(candidate, context, options);

            var baseMerit = candidate.Score / Math.Max(penalty, double.Epsilon);
            candidate.Score = (0.20 * baseMerit + 0.80 * relationship) * penalty;
        }
    }
}
