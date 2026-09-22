// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;
using MusicStreaming.Application.Recommendations.Scoring;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Options;

/// <summary>Everything tunable about recommendations, grouped by who reads it.</summary>
/// <remarks>
/// Группы — не таксономия, а потребители: <see cref="Diversity"/> читает только MMR-отбор,
/// <see cref="Exploration"/> — только деление на ближнее и дальнее, <see cref="Penalties"/> —
/// только скоринг кандидата. Благодаря этому чистые классы в <c>Recommendations/Scoring</c>
/// зависят от пяти-пятнадцати чисел, а не от всего конфига проекта, и переносятся отдельно.
/// <para>
/// Каждая группа проверяет себя сама, рядом со своими свойствами.
/// </para>
/// </remarks>
public class RecommendationOptions
{
    public const string SectionName = "Recommendations";

    public bool Enabled { get; set; } = true;

    public RecommendationDecayOptions Decay { get; set; } = new();
    public RecommendationShelfOptions Shelves { get; set; } = new();
    public DiversityOptions Diversity { get; set; } = new();
    public ExplorationOptions Exploration { get; set; } = new();
    public CandidatePenaltyOptions Penalties { get; set; } = new();
    public TasteVectorOptions Vector { get; set; } = new();
    public CollaborativeOptions Collaborative { get; set; } = new();
    public RecommendationMaintenanceOptions Maintenance { get; set; } = new();

    public RankingWeights Cold { get; set; } = RankingWeights.ColdDefaults();
    public RankingWeights Warm { get; set; } = RankingWeights.WarmDefaults();
    public RankingWeights Mature { get; set; } = RankingWeights.MatureDefaults();

    public RankingWeights WeightsFor(ProfileMaturity maturity) => maturity switch
    {
        ProfileMaturity.Mature => Mature,
        ProfileMaturity.Warm => Warm,
        _ => Cold,
    };

    public static OptionsBuilder<RecommendationOptions> Validated(
        OptionsBuilder<RecommendationOptions> builder) =>
        RecommendationMaintenanceOptions.Validate(
            CollaborativeOptions.Validate(
                TasteVectorOptions.Validate(
                    CandidatePenaltyOptions.Validate(
                        ExplorationOptions.Validate(
                            DiversityOptions.Validate(
                                RecommendationShelfOptions.Validate(
                                    RecommendationDecayOptions.Validate(builder))))))));
}
