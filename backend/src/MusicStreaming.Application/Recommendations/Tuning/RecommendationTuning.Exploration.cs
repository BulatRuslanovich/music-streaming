// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations;

public static partial class RecommendationTuning
{
    /// <summary>
    /// How much of a shelf or a queue is given to tracks that sound unlike the listener's taste.
    /// </summary>
    public static class Exploration
    {
        public const double ShelfRatio = 0.25;
        public const double ShelfDiscoveryRatio = 0.60;

        /// <summary>
        /// Доля exploration в очереди радио. Отдельно от <see cref="ShelfRatio"/>: та настроена
        /// под полки и проверена eval'ом, а очередь — другой потребитель с другим ощущением.
        /// </summary>
        public const double QueueRatio = 0.15;

        /// <summary>Доля exploration в очереди, пока вектор ещё только знакомится со слушателем.</summary>
        public const double QueueDiscoverRatio = 0.35;

        /// <summary>Доля пула, попадающая в far-корзину: нижний квартиль по близости к вкусу.</summary>
        public const double FarQuantile = 0.25;

        /// <summary>
        /// Разброс, которым перемешивается порядок far-корзины. Читают и полки, и очередь радио:
        /// корзина у них одна и та же по смыслу, и расходиться этим числом им незачем.
        /// </summary>
        public const double FarJitter = 0.30;

        /// <summary>Сколько треков отдаётся за одно обращение к радио.</summary>
        public const int QueueSize = 6;
    }
}
