// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations;

public static partial class RecommendationTuning
{
    /// <summary>
    /// How much repetition one shelf or one queue may contain.
    /// </summary>
    public static class Diversity
    {
        public const int MaxPerArtist = 2;
        public const int MaxPerAlbum = 2;
        public const int MaxPerGenre = 4;
        public const double DiversityLambda = 0.30;

        /// <summary>
        /// Накопительный штраф в MMR за каждое предыдущее появление артиста. Основную работу делает
        /// на последней ступени послаблений, где жёстких лимитов уже нет.
        /// </summary>
        public const double ArtistRepeatPenalty = 0.15;
    }
}
