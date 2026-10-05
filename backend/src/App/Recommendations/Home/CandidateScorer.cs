// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Domain.Entities.Recommendations;

namespace App.Recommendations.Home;

public static class CandidateScorer
{
    public static void Score(
        RecommendationCandidate candidate,
        RankingContext context,
        ProfileMaturity maturity)
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

        // Холодный профиль ранжируется почти без личных сигналов — только по звучанию первых затравок,
        // если они уже есть; с опытом растёт вес вкуса и плейлистов.
        var w = maturity switch
        {
            ProfileMaturity.Mature => (Taste: 0.31, Content: 0.12, Audio: 0.10, Collaborative: 0.20, Behavior: 0.20,
                Popularity: 0.04, Freshness: 0.03, Coverage: 0.0),
            ProfileMaturity.Warm => (Taste: 0.26, Content: 0.22, Audio: 0.10, Collaborative: 0.13, Behavior: 0.17,
                Popularity: 0.08, Freshness: 0.04, Coverage: 0.0),
            _ => (Taste: 0.15, Content: 0.0, Audio: 0.05, Collaborative: 0.0, Behavior: 0.0,
                Popularity: 0.30, Freshness: 0.20, Coverage: 0.30),
        };

        // Звуковые признаки есть не у всех треков: отсутствующий признак выпадает из среднего,
        // а не тянет счёт к нулю.
        var sum = w.Content * candidate.Content
                  + w.Collaborative * candidate.Collaborative
                  + w.Behavior * candidate.Behavior
                  + w.Popularity * candidate.Popularity
                  + w.Freshness * candidate.Freshness
                  + w.Coverage * candidate.Coverage;

        var present = w.Content + w.Collaborative + w.Behavior + w.Popularity + w.Freshness + w.Coverage;

        if (candidate.TasteFit is { } taste)
        {
            sum += w.Taste * taste;
            present += w.Taste;
        }

        if (candidate.AudioSimilarity is { } audio)
        {
            sum += w.Audio * audio;
            present += w.Audio;
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
