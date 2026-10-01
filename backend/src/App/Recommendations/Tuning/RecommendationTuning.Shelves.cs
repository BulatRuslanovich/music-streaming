// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Recommendations;

public static partial class RecommendationTuning
{
    public static class Shelves
    {
        public const int ShelfSize = 12;

        public const int MixPoolSize = 120;

        public const int CandidateLimit = 600;
        public const int PerSourceLimit = 120;
        public const double FreshnessWindowDays = 30;
        public const int CacheTtlHours = 6;
    }
}
