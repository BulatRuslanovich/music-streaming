// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations;

public static partial class RecommendationTuning
{
    /// <summary>
    /// Multipliers and gates applied to an already-ranked candidate.
    /// </summary>
    public static class Penalties
    {
        public const double JustPlayed = 0.15;
        public const double RecentlyPlayed = 0.60;
        public const double DislikedTrack = 0.10;
        public const double DislikedArtist = 0.30;

        /// <summary>С какой доли пропусков по библиотеке трек начинает считаться слабым.</summary>
        public const double HighSkipRateThreshold = 0.50;

        /// <summary>Множитель для трека, который бросают всегда.</summary>
        public const double HighSkipRatePenalty = 0.60;

        /// <summary>Меньше этого числа прослушиваний глобальная статистика трека не считается показательной.</summary>
        public const int MinimumStatsSupport = 5;

        /// <summary>Нижняя граница множителя соответствия эпохе: сигнал мягкий, а не запрещающий.</summary>
        public const double EraFitFloor = 0.75;

        /// <summary>Минимальный разброс годов, чтобы узкий профиль не отсекал всё вокруг.</summary>
        public const double MinimumYearSpread = 6;

        public const int JustPlayedHours = 24;
        public const int RecentlyPlayedDays = 7;
        public const double MultiSourceBonus = 0.08;

        /// <summary>Сколько дней держится «не интересно» по треку. 0 — навсегда; артист блокируется навсегда всегда.</summary>
        public const int TrackSuppressionDays = 180;
    }
}
