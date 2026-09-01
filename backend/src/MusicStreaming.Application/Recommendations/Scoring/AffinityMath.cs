// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Recommendations.Scoring;

public static class AffinityMath
{
    public static double Normalize(double weight, double softness)
    {
        if (softness <= 0)
            throw new ArgumentOutOfRangeException(nameof(softness), "Мягкость должна быть положительной.");

        return weight / (Math.Abs(weight) + softness);
    }

    /// <summary>
    /// Зрелость профиля по затухающей массе положительных сигналов, а не по пожизненному счётчику:
    /// профиль, который полгода молчит, должен вернуться к осторожным весам.
    /// </summary>
    public static ProfileMaturity MaturityFor(
        double positiveSignals, int warmThreshold, int matureThreshold)
    {
        if (positiveSignals >= matureThreshold)
            return ProfileMaturity.Mature;

        return positiveSignals >= warmThreshold ? ProfileMaturity.Warm : ProfileMaturity.Cold;
    }

    public static double Shrink(double value, int support, double lambda)
    {
        if (support <= 0)
            return 0;

        return value * (support / (support + lambda));
    }

    public static double Freshness(DateTimeOffset addedAt, DateTimeOffset now, double windowDays)
    {
        if (windowDays <= 0)
            return 0;

        var age = (now - addedAt).TotalDays;
        if (age <= 0)
            return 1;

        return age >= windowDays ? 0 : 1 - age / windowDays;
    }
}
