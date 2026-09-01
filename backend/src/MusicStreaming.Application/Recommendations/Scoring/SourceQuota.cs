// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations.Scoring;

/// <summary>Как источник делит свой бюджет между артистами или жанрами, за которые он отвечает.</summary>
public static class SourceQuota
{
    /// <summary>
    /// Доля источника в пуле пропорциональна силе привязанности: жанр или артист, до которых
    /// человек дотрагивался пару раз, не должны занимать столько же места, сколько основные.
    /// Небольшой пол оставлен намеренно — совсем выбрасывать слабые ветки значит сузить пул.
    /// </summary>
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
