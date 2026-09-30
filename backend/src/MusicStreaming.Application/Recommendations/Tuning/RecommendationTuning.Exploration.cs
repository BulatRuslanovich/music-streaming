// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations;

public static partial class RecommendationTuning
{
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
}
