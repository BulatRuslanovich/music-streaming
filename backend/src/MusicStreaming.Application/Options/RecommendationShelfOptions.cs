// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;

namespace MusicStreaming.Application.Options;

/// <summary>Sizes of the shelves and of the candidate pool they are drawn from.</summary>
public class RecommendationShelfOptions
{
    public int ShelfSize { get; set; } = 12;
    public int CandidateLimit { get; set; } = 600;
    public int PerSourceLimit { get; set; } = 120;
    public int SimilarTopK { get; set; } = 50;
    public double FreshnessWindowDays { get; set; } = 30;

    /// <summary>За сколько дней собирается вкус по частям суток.</summary>
    public int DaypartWindowDays { get; set; } = 90;

    /// <summary>Ниже этой доли прослушивания часть суток не заслуживает собственной полки.</summary>
    public double MinimumDaypartShare { get; set; } = 0.10;

    public int CacheTtlHours { get; set; } = 6;

    internal static OptionsBuilder<RecommendationOptions> Validate(
        OptionsBuilder<RecommendationOptions> builder) => builder
        .Validate(o => o.Shelves.ShelfSize > 0, "Recommendations:Shelves:ShelfSize must be greater than zero.")
        .Validate(
            o => o.Shelves.CandidateLimit >= o.Shelves.ShelfSize,
            "Recommendations:Shelves:CandidateLimit must be at least Recommendations:Shelves:ShelfSize.")
        .Validate(o => o.Shelves.PerSourceLimit > 0, "Recommendations:Shelves:PerSourceLimit must be greater than zero.")
        .Validate(o => o.Shelves.SimilarTopK > 0, "Recommendations:Shelves:SimilarTopK must be greater than zero.")
        .Validate(o => o.Shelves.FreshnessWindowDays > 0, "Recommendations:Shelves:FreshnessWindowDays must be greater than zero.")
        .Validate(o => o.Shelves.DaypartWindowDays > 0, "Recommendations:Shelves:DaypartWindowDays must be positive.")
        .Validate(
            o => o.Shelves.MinimumDaypartShare is >= 0 and <= 1,
            "Recommendations:Shelves:MinimumDaypartShare must be between 0 and 1.")
        .Validate(o => o.Shelves.CacheTtlHours > 0, "Recommendations:Shelves:CacheTtlHours must be greater than zero.");
}
