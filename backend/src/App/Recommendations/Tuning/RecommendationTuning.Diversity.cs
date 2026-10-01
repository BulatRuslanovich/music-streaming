// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Recommendations;

public static partial class RecommendationTuning
{
    public static class Diversity
    {
        public const int MaxPerArtist = 2;
        public const int MaxPerAlbum = 2;
        public const int MaxPerGenre = 4;
        public const double DiversityLambda = 0.30;

        public const double ArtistRepeatPenalty = 0.15;
    }
}
