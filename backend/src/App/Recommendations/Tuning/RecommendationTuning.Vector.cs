// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Recommendations;

public static partial class RecommendationTuning
{
    public static class Vector
    {
        public const double Alpha = 0.22;

        public const int FormingAt = 3;

        public const int ReadyAt = 8;

        public const int ClusterCount = 8;

        public const int IndexReloadMinutes = 15;
    }
}
