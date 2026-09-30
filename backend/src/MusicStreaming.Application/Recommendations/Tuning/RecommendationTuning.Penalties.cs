// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations;

public static partial class RecommendationTuning
{
    public static class Penalties
    {
        public const double JustPlayed = 0.15;
        public const double RecentlyPlayed = 0.60;
        public const double DislikedTrack = 0.10;
        public const double DislikedArtist = 0.30;

        public const double HighSkipRateThreshold = 0.50;

        public const double HighSkipRatePenalty = 0.60;

        public const int MinimumStatsSupport = 5;

        public const double EraFitFloor = 0.75;

        public const double MinimumYearSpread = 6;

        public const int JustPlayedHours = 24;
        public const int RecentlyPlayedDays = 7;
        public const double MultiSourceBonus = 0.08;
    }
}
