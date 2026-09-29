// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations;

public static partial class RecommendationTuning
{
    /// <summary>When the background passes run and how long their inputs are kept.</summary>
    public static class Maintenance
    {
        public const int RegenerationDebounceSeconds = 60;

        /// <summary>Потолок задержки пересборки: непрерывная активность не должна откладывать её вечно.</summary>
        public const int RegenerationMaxDelaySeconds = 300;

        public const int IntervalHours = 6;
        public const int StartupDelaySeconds = 30;
        public const int EventRetentionDays = 180;

        /// <summary>How long the per-hour listening rollup is kept.</summary>
        /// <remarks>
        /// Всё, что её читает — итоги месяца, статистика слушателя, вкус по частям суток, — берёт
        /// окно, а не всю историю. Два года покрывают любое такое окно с запасом, а держалась она
        /// вечно: порядка семидесяти строк на слушателя в день, и ни одного удаления.
        /// </remarks>
        public const int ListeningStatRetentionDays = 730;
        public const int MaxEventsPerRequest = 100;
    }
}
