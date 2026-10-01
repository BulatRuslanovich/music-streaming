// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Recommendations.Scoring;

public static class RecencyDecay
{
    private static double Factor(TimeSpan age, double halfLifeDays)
    {
        if (halfLifeDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(halfLifeDays), "The half-life must be positive.");

        var days = age.TotalDays;
        return days <= 0 ? 1.0 : Math.Pow(2, -days / halfLifeDays);
    }

    public static (double Weight, DateTimeOffset Anchor) Accumulate(
        double weight,
        DateTimeOffset anchor,
        double addedWeight,
        DateTimeOffset at,
        double halfLifeDays)
    {
        return at >= anchor ? (weight * Factor(at - anchor, halfLifeDays) + addedWeight, at) : (weight + addedWeight * Factor(anchor - at, halfLifeDays), anchor);
    }

    public static double ValueAt(double weight, DateTimeOffset anchor, DateTimeOffset now, double halfLifeDays) =>
        weight * Factor(now - anchor, halfLifeDays);
}
