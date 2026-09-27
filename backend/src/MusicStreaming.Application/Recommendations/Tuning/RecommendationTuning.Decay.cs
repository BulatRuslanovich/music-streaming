// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations;

public static partial class RecommendationTuning
{
    /// <summary>How fast listening history loses weight, and when a profile counts as mature.</summary>
    public static class Decay
    {
        public const double TrackHalfLifeDays = 45;

        /// <summary>Half-life of an edge in the track transition graph.</summary>
        /// <remarks>
        /// Граф переходов был единственным сигналом без затухания: вес только прибавлялся, и пара,
        /// наигранная два года назад, навсегда перевешивала свежее поведение. Полураспад здесь длиннее
        /// трекового: соседство двух треков — свойство более устойчивое, чем интерес к одному из них.
        /// </remarks>
        public const double TransitionHalfLifeDays = 120;
        public const double ArtistHalfLifeDays = 90;
        public const double GenreHalfLifeDays = 90;

        /// <summary>Период полураспада массы сигналов, определяющей зрелость профиля.</summary>
        public const double ProfileHalfLifeDays = 120;
        public const double ScoreSoftness = 3;
        public const int WarmThreshold = 10;
        public const int MatureThreshold = 100;
    }
}
