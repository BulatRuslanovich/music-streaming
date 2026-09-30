// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Recommendations;
using MusicStreaming.Application.Recommendations.Scoring;

namespace MusicStreaming.Application.Services.Recommendations;

internal static class RecommendationSeedSelector
{
    private static readonly TimeSpan RecencyHalfLife = TimeSpan.FromDays(30);

    public static List<RecommendationSeed> Select(
        IReadOnlyDictionary<Guid, TrackHistory> history,
        DateTimeOffset now,
        int count)
    {
        var seeds = new List<RecommendationSeed>();

        foreach (var (trackId, track) in history)
        {
            if (track.Score <= 0 || (track.SkipCount >= 2 && track.AverageCompletion < 0.20 && track.Score < 0.35))
                continue;

            var engagement = Math.Max(
                Math.Clamp(track.AverageCompletion, 0, 1),
                Math.Max(
                    track.CompletedCount > 0 ? 0.85 : 0,
                    Math.Max(track.ReplayCount > 0 ? 0.95 : 0, track.PlaylistAdds > 0 ? 1 : 0)));

            engagement = Math.Max(engagement, Math.Clamp(track.Score * 2, 0, 1));

            var repetition = 1 - Math.Exp(-Math.Max(1, track.PlayCount) / 3.0);
            var age = Math.Max(0, (now - track.LastPlayedAt).TotalSeconds);
            var recency = Math.Pow(0.5, age / RecencyHalfLife.TotalSeconds);

            var weight = track.Score
                         * (0.35 + 0.45 * engagement + 0.20 * repetition)
                         * (0.45 + 0.55 * recency);

            if (weight > 0)
                seeds.Add(new RecommendationSeed(trackId, weight));
        }

        return seeds
            .OrderByDescending(seed => seed.Weight)
            .ThenBy(seed => seed.TrackId)
            .Take(Math.Max(0, count))
            .ToList();
    }
}
