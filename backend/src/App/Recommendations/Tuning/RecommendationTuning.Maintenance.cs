// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Recommendations;

public static partial class RecommendationTuning
{
    public static class Maintenance
    {
        public const int RegenerationDebounceSeconds = 60;

        public const int RegenerationMaxDelaySeconds = 300;

        public const int IntervalHours = 6;
        public const int StartupDelaySeconds = 30;
        public const int EventRetentionDays = 180;

        public const int ListeningStatRetentionDays = 730;
        public const int MaxEventsPerRequest = 100;
    }
}
