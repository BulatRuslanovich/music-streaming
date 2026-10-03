// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Recommendations;

public static class EventWeights
{
    public static double CompletionRatio(int listenedSeconds, int durationSeconds)
    {
        if (durationSeconds <= 0 || listenedSeconds <= 0)
            return 0;

        return Math.Min(1.0, (double)listenedSeconds / durationSeconds);
    }
}
