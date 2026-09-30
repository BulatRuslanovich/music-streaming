// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations.Scoring;

public static class SourceQuota
{
    public static int Of(int budget, double affinity, int shares)
    {
        var even = (double)budget / Math.Max(1, shares);

        return Math.Max(1, (int)Math.Ceiling(even * (0.25 + 0.75 * Math.Clamp(affinity, 0, 1))));
    }

    public static List<Guid> TopScoring(IReadOnlyDictionary<Guid, double> scores, int count) =>
        scores
            .Where(pair => pair.Value > 0)
            .OrderByDescending(pair => pair.Value)
            .Take(count)
            .Select(pair => pair.Key)
            .ToList();
}
