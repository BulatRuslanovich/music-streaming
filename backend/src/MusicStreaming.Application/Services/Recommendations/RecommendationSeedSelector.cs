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
        int count) =>
        history
            .Where(pair => Trustworthy(pair.Value))
            .Select(pair => new RecommendationSeed(pair.Key, WeightOf(pair.Value, now)))
            .Where(seed => seed.Weight > 0)
            .OrderByDescending(seed => seed.Weight)
            .ThenBy(seed => seed.TrackId)
            .Take(Math.Max(0, count))
            .ToList();

    private static bool Trustworthy(TrackHistory history) =>
        history.Score > 0
        && !(history.SkipCount >= 2 && history.AverageCompletion < 0.20 && history.Score < 0.35);

    private static double WeightOf(TrackHistory history, DateTimeOffset now)
    {
        var engagement = Math.Max(
            Math.Clamp(history.AverageCompletion, 0, 1),
            Math.Max(
                history.CompletedCount > 0 ? 0.85 : 0,
                Math.Max(history.ReplayCount > 0 ? 0.95 : 0, history.PlaylistAdds > 0 ? 1 : 0)));

        engagement = Math.Max(engagement, Math.Clamp(history.Score * 2, 0, 1));

        var repetition = 1 - Math.Exp(-Math.Max(1, history.PlayCount) / 3.0);
        var age = Math.Max(0, (now - history.LastPlayedAt).TotalSeconds);
        var recency = Math.Pow(0.5, age / RecencyHalfLife.TotalSeconds);

        return history.Score
               * (0.35 + 0.45 * engagement + 0.20 * repetition)
               * (0.45 + 0.55 * recency);
    }
}
