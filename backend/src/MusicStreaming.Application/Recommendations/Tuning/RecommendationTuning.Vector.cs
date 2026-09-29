// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations;

public static partial class RecommendationTuning
{
    /// <summary>The learned taste vector: how fast it moves, when it is trusted, how it is indexed.</summary>
    public static class Vector
    {
        /// <summary>
        /// Скорость забывания векторного вкуса. 0.22 означает, что десяток событий почти полностью
        /// переписывает вектор: отзывчиво, но коротко.
        /// </summary>
        public const double Alpha = 0.22;

        /// <summary>Сколько положительных сигналов делают вектор формирующимся.</summary>
        public const int FormingAt = 3;

        /// <summary>Сколько положительных сигналов делают вектор зрелым.</summary>
        public const int ReadyAt = 8;

        /// <summary>Сколько кластеров строит сферический k-means по эмбеддингам.</summary>
        public const int ClusterCount = 8;

        /// <summary>Как часто перечитывать матрицу эмбеддингов. Пересборка идёт только если что-то изменилось.</summary>
        public const int IndexReloadMinutes = 15;
    }
}
