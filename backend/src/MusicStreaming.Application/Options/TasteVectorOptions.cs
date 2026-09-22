// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;

namespace MusicStreaming.Application.Options;

/// <summary>The learned taste vector: how fast it moves, when it is trusted, how it is indexed.</summary>
public class TasteVectorOptions
{
    /// <summary>
    /// Скорость забывания векторного вкуса. 0.22 означает, что десяток событий почти полностью
    /// переписывает вектор: отзывчиво, но коротко.
    /// </summary>
    public double Alpha { get; set; } = 0.22;

    /// <summary>Сколько положительных сигналов делают вектор формирующимся.</summary>
    public int FormingAt { get; set; } = 3;

    /// <summary>Сколько положительных сигналов делают вектор зрелым.</summary>
    public int ReadyAt { get; set; } = 8;

    /// <summary>Вес общего вектора в запросе; остаток достаётся вектору текущей части суток.</summary>
    public double DaypartBlendShare { get; set; } = 0.7;

    /// <summary>Сколько кластеров строит сферический k-means по эмбеддингам.</summary>
    public int ClusterCount { get; set; } = 8;

    /// <summary>Как часто перечитывать матрицу эмбеддингов. Пересборка идёт только если что-то изменилось.</summary>
    public int IndexReloadMinutes { get; set; } = 15;

    internal static OptionsBuilder<RecommendationOptions> Validate(
        OptionsBuilder<RecommendationOptions> builder) => builder
        .Validate(o => o.Vector.Alpha is > 0 and <= 1, "Recommendations:Vector:Alpha must be in (0, 1].")
        .Validate(o => o.Vector.FormingAt > 0, "Recommendations:Vector:FormingAt must be greater than zero.")
        .Validate(o => o.Vector.ReadyAt >= o.Vector.FormingAt, "Recommendations:Vector:ReadyAt must be at least Recommendations:Vector:FormingAt.")
        .Validate(o => o.Vector.DaypartBlendShare is >= 0 and <= 1, "Recommendations:Vector:DaypartBlendShare must be in [0, 1].")
        .Validate(o => o.Vector.ClusterCount is >= 2 and <= 256, "Recommendations:Vector:ClusterCount must be between 2 and 256.")
        .Validate(o => o.Vector.IndexReloadMinutes > 0, "Recommendations:Vector:IndexReloadMinutes must be greater than zero.");
}
