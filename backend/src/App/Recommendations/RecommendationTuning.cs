// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Recommendations;

public static class RecommendationTuning
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

    public static class Diversity
    {
        public const int MaxPerArtist = 2;
        public const int MaxPerAlbum = 2;
        public const int MaxPerGenre = 4;
        public const double DiversityLambda = 0.30;

        public const double ArtistRepeatPenalty = 0.15;
    }

    public static class Exploration
    {
        public const double ShelfRatio = 0.25;
        public const double ShelfDiscoveryRatio = 0.60;

        public const double QueueRatio = 0.15;

        public const double QueueDiscoverRatio = 0.35;

        public const double FarQuantile = 0.25;

        public const double FarJitter = 0.30;

        public const int QueueSize = 6;
    }

    public static class Maintenance
    {
        public const int RegenerationDebounceSeconds = 60;

        public const int RegenerationMaxDelaySeconds = 300;

        public const int IntervalHours = 6;
        public const int StartupDelaySeconds = 30;
        public const int EventRetentionDays = 180;
        public const int MaxEventsPerRequest = 100;
    }

    public static class Penalties
    {
        public const double JustPlayed = 0.15;
        public const double RecentlyPlayed = 0.60;
        public const double DislikedTrack = 0.10;
        public const double DislikedArtist = 0.30;

        public const double EraFitFloor = 0.75;

        public const double MinimumYearSpread = 6;

        public const int JustPlayedHours = 24;
        public const int RecentlyPlayedDays = 7;
        public const double MultiSourceBonus = 0.08;
    }

    public static class Shelves
    {
        public const int ShelfSize = 12;

        public const int MixPoolSize = 120;

        public const int PerSourceLimit = 120;
        public const double FreshnessWindowDays = 30;
        public const int CacheTtlHours = 6;
    }
}
