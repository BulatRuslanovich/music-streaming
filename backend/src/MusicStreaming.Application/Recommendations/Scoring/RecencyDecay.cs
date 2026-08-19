// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations.Scoring;

public static class RecencyDecay
{
    public static double Factor(TimeSpan age, double halfLifeDays)
    {
        if (halfLifeDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(halfLifeDays), "Период полураспада должен быть положительным.");

        var days = age.TotalDays;
        if (days <= 0)
            return 1.0;

        return Math.Pow(2, -days / halfLifeDays);
    }

    public static (double Weight, DateTimeOffset Anchor) Accumulate(
        double weight,
        DateTimeOffset anchor,
        double addedWeight,
        DateTimeOffset at,
        double halfLifeDays)
    {
        if (at >= anchor)
            return (weight * Factor(at - anchor, halfLifeDays) + addedWeight, at);

        return (weight + addedWeight * Factor(anchor - at, halfLifeDays), anchor);
    }

    public static double ValueAt(double weight, DateTimeOffset anchor, DateTimeOffset now, double halfLifeDays) =>
        weight * Factor(now - anchor, halfLifeDays);
}
