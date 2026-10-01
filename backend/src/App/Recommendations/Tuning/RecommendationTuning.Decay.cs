// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Recommendations;

public static partial class RecommendationTuning
{
    public static class Decay
    {
        public const double TrackHalfLifeDays = 45;

        public const double TransitionHalfLifeDays = 120;
        public const double ArtistHalfLifeDays = 90;
        public const double GenreHalfLifeDays = 90;

        public const double ProfileHalfLifeDays = 120;
        public const double ScoreSoftness = 3;
        public const int WarmThreshold = 10;
        public const int MatureThreshold = 100;
    }
}
