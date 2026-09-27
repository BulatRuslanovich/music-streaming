// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations;

public static partial class RecommendationTuning
{
    /// <summary>Sizes of the shelves and of the candidate pool they are drawn from.</summary>
    public static class Shelves
    {
        public const int ShelfSize = 12;
        public const int CandidateLimit = 600;
        public const int PerSourceLimit = 120;
        public const int SimilarTopK = 50;
        public const double FreshnessWindowDays = 30;

        /// <summary>За сколько дней собирается вкус по частям суток.</summary>
        public const int DaypartWindowDays = 90;

        /// <summary>Ниже этой доли прослушивания часть суток не заслуживает собственной полки.</summary>
        public const double MinimumDaypartShare = 0.10;

        public const int CacheTtlHours = 6;
    }
}
