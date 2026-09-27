// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations;

public static partial class RecommendationTuning
{
    /// <summary>Thresholds for the "listeners like you" signal.</summary>
    public static class Collaborative
    {
        public const double Shrinkage = 5;
        public const double BlendPivot = 10;
        public const int MinUsers = 5;
        public const int MinInteractions = 30;
    }
}
