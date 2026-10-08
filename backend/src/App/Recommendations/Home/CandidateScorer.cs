// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Domain.Entities.Recommendations;

namespace App.Recommendations.Home;

public static class CandidateScorer
{
    public static void Score(
        RecommendationCandidate candidate,
        RankingContext context,
        ProfileMaturity maturity,
        RankingWeights? weights = null)
    {
        // Вкус к артистам (соавторы весят вдвое меньше, а их нелюбовь — ещё вдвое) и к жанру.
        var artistTotal = 0.0;
        var artistWeight = 0.0;

        foreach (var artistId in candidate.ArtistIds.Count > 0 ? candidate.ArtistIds : [candidate.ArtistId])
        {
            if (!context.ArtistScores.TryGetValue(artistId, out var score))
                continue;

            var share = artistId == candidate.ArtistId ? 1.0 : 0.5;

            if (score < 0 && artistId != candidate.ArtistId)
                share *= 0.5;

            artistTotal += score * share;
            artistWeight += share;
        }

        var artist = artistWeight > 0 ? artistTotal / artistWeight : 0;

        var genre = candidate.GenreId is { } genreId && context.GenreScores.TryGetValue(genreId, out var g)
            ? g
            : 0;

        candidate.Behavior = Math.Clamp(artist * 0.7 + genre * 0.3, -1, 1);

        var w = weights ?? RankingWeights.Hand(maturity);
        var features = RankingFeatures.Of(candidate);

        // Отсутствующий признак (нет эмбеддинга) выпадает из среднего, а не тянет счёт к нулю.
        var sum = 0.0;
        var present = 0.0;

        for (var feature = 0; feature < RankingFeatures.Count; feature++)
        {
            if (features[feature] is not { } value)
                continue;

            sum += w[feature] * value;
            present += w[feature];
        }

        var merit = present <= 0 ? 0 : sum / present;

        var penalty = 1.0;

        if (context.History.TryGetValue(candidate.TrackId, out var history))
        {
            var sinceLastPlay = context.Now - history.LastPlayedAt;

            if (sinceLastPlay < TimeSpan.FromHours(RecommendationTuning.Penalties.JustPlayedHours))
                penalty *= RecommendationTuning.Penalties.JustPlayed;
            else if (sinceLastPlay < TimeSpan.FromDays(RecommendationTuning.Penalties.RecentlyPlayedDays))
                penalty *= RecommendationTuning.Penalties.RecentlyPlayed;

            // Дважды брошенный в начале или явно отвергнутый трек.
            if (history is { SkipCount: >= 2, AverageCompletion: < 0.2 } || history.Score < RecommendationTuning.Penalties.RejectedTrackScore)
                penalty *= RecommendationTuning.Penalties.DislikedTrack;
        }

        if (candidate.Behavior < -0.3)
            penalty *= RecommendationTuning.Penalties.DislikedArtist;

        // Мягкая гауссова подгонка под привычную эпоху слушателя.
        if (context.YearCenter is { } center && candidate.Year is { } year)
        {
            var spread = Math.Max(context.YearSpread, RecommendationTuning.Penalties.MinimumYearSpread);
            var distance = (year - center) / spread;
            var fit = Math.Exp(-0.5 * distance * distance);

            penalty *= RecommendationTuning.Penalties.EraFitFloor + (1 - RecommendationTuning.Penalties.EraFitFloor) * fit;
        }

        // Трек, найденный несколькими независимыми каналами, получает небольшую надбавку.
        var consensus = 1 + Math.Clamp(candidate.EvidenceCount - 1, 0, 3) * RecommendationTuning.Penalties.MultiSourceBonus;

        candidate.Score = merit * consensus * penalty;
    }
}
