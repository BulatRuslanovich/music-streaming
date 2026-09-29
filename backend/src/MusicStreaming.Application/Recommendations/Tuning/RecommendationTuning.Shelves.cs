// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations;

public static partial class RecommendationTuning
{
    /// <summary>Sizes of the shelves and of the candidate pool they are drawn from.</summary>
    public static class Shelves
    {
        public const int ShelfSize = 12;

        /// <summary>Сколько кандидатов лежит в скрытом пуле, из которого раз в сутки тянется микс дня.</summary>
        /// <remarks>Вдвое больше самого микса: иначе взвешенная выборка почти не выбирала бы.</remarks>
        public const int MixPoolSize = 120;

        public const int CandidateLimit = 600;
        public const int PerSourceLimit = 120;
        public const double FreshnessWindowDays = 30;
        public const int CacheTtlHours = 6;
    }
}
