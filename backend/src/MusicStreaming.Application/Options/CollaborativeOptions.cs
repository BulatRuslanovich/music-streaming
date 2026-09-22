// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;

namespace MusicStreaming.Application.Options;

/// <summary>Thresholds for the "listeners like you" signal.</summary>
public class CollaborativeOptions
{
    public double Shrinkage { get; set; } = 5;
    public double BlendPivot { get; set; } = 10;
    public int MinUsers { get; set; } = 5;
    public int MinInteractions { get; set; } = 30;

    internal static OptionsBuilder<RecommendationOptions> Validate(
        OptionsBuilder<RecommendationOptions> builder) => builder
        .Validate(o => o.Collaborative.Shrinkage > 0, "Recommendations:Collaborative:Shrinkage must be greater than zero.")
        .Validate(o => o.Collaborative.BlendPivot > 0, "Recommendations:Collaborative:BlendPivot must be greater than zero.")
        .Validate(o => o.Collaborative.MinUsers > 0, "Recommendations:Collaborative:MinUsers must be greater than zero.")
        .Validate(o => o.Collaborative.MinInteractions > 0, "Recommendations:Collaborative:MinInteractions must be greater than zero.");
}
