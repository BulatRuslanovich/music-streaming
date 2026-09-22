// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;

namespace MusicStreaming.Application.Options;

/// <summary>
/// How much of a shelf or a queue is given to tracks that sound unlike the listener's taste.
/// Read by the pure near/far split, which takes this and nothing else from configuration.
/// </summary>
public class ExplorationOptions
{
    public double ShelfRatio { get; set; } = 0.25;
    public double ShelfDiscoveryRatio { get; set; } = 0.60;

    /// <summary>
    /// Доля exploration в очереди радио. Отдельно от <see cref="ShelfRatio"/>: та настроена
    /// под полки и проверена eval'ом, а очередь — другой потребитель с другим ощущением.
    /// </summary>
    public double QueueRatio { get; set; } = 0.15;

    /// <summary>Доля exploration в очереди, пока вектор ещё только знакомится со слушателем.</summary>
    public double QueueDiscoverRatio { get; set; } = 0.35;

    /// <summary>Доля пула, попадающая в far-корзину: нижний квартиль по близости к вкусу.</summary>
    public double FarQuantile { get; set; } = 0.25;

    /// <summary>
    /// Разброс, которым перемешивается порядок far-корзины. Читают и полки, и очередь радио:
    /// корзина у них одна и та же по смыслу, и расходиться этим числом им незачем.
    /// </summary>
    public double FarJitter { get; set; } = 0.30;

    /// <summary>Сколько треков отдаётся за одно обращение к радио.</summary>
    public int QueueSize { get; set; } = 6;

    internal static OptionsBuilder<RecommendationOptions> Validate(
        OptionsBuilder<RecommendationOptions> builder) => builder
        .Validate(o => o.Exploration.ShelfRatio is >= 0 and <= 1, "Recommendations:Exploration:ShelfRatio must be between 0 and 1.")
        .Validate(o => o.Exploration.ShelfDiscoveryRatio is >= 0 and <= 1, "Recommendations:Exploration:ShelfDiscoveryRatio must be between 0 and 1.")
        .Validate(o => o.Exploration.QueueRatio is >= 0 and <= 1, "Recommendations:Exploration:QueueRatio must be in [0, 1].")
        .Validate(o => o.Exploration.QueueDiscoverRatio is >= 0 and <= 1, "Recommendations:Exploration:QueueDiscoverRatio must be in [0, 1].")
        .Validate(o => o.Exploration.FarQuantile is > 0 and < 1, "Recommendations:Exploration:FarQuantile must be in (0, 1).")
        .Validate(o => o.Exploration.FarJitter is >= 0 and <= 1, "Recommendations:Exploration:FarJitter must be in [0, 1].")
        .Validate(o => o.Exploration.QueueSize is >= 1 and <= 100, "Recommendations:Exploration:QueueSize must be between 1 and 100.");
}
