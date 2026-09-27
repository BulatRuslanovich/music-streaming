// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Options;

/// <summary>Turns the recommendation pipeline on or off.</summary>
/// <remarks>
/// Всё остальное, что раньше жило здесь, — константы в <see cref="Recommendations.RecommendationTuning"/>.
/// </remarks>
public class RecommendationOptions
{
    public const string SectionName = "Recommendations";

    public bool Enabled { get; set; } = true;
}
