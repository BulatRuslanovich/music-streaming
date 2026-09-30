// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations.Scoring;

public enum VectorMaturityLevel
{
    Discovering = 0,

    Forming = 1,
    Ready = 2,
}

public static class VectorMaturity
{
    public static VectorMaturityLevel Of(int positiveCount, int formingAt, int readyAt)
    {
        if (positiveCount >= readyAt)
            return VectorMaturityLevel.Ready;

        return positiveCount >= formingAt ? VectorMaturityLevel.Forming : VectorMaturityLevel.Discovering;
    }

    public static double EffectiveExplore(double baseRatio, double discover, VectorMaturityLevel maturity) =>
        maturity switch
        {
            VectorMaturityLevel.Discovering => Math.Max(discover, baseRatio),
            VectorMaturityLevel.Forming => (baseRatio + discover) / 2,
            _ => baseRatio,
        };
}
