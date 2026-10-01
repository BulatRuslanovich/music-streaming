// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Domain.Entities.Recommendations;

namespace App.Recommendations.Scoring;

public class RankingWeights
{
    public double Taste { get; set; }

    public double Content { get; set; }

    public double Audio { get; set; }

    public double Collaborative { get; set; }

    public double Behavior { get; set; }

    public double Popularity { get; set; }

    public double Freshness { get; set; }

    public double Coverage { get; set; }

    public double Total =>
        Taste + Content + Audio + Collaborative + Behavior + Popularity + Freshness + Coverage;

    public static RankingWeights For(ProfileMaturity maturity) => maturity switch
    {
        ProfileMaturity.Mature => MatureDefaults(),
        ProfileMaturity.Warm => WarmDefaults(),
        _ => ColdDefaults(),
    };

    public static RankingWeights ColdDefaults() => new()
    {
        Popularity = 0.40,
        Freshness = 0.25,
        Coverage = 0.35,
    };

    public static RankingWeights WarmDefaults() => new()
    {
        Taste = 0.26,
        Content = 0.22,
        Audio = 0.10,
        Collaborative = 0.13,
        Behavior = 0.17,
        Popularity = 0.08,
        Freshness = 0.04,
    };

    public static RankingWeights MatureDefaults() => new()
    {
        Taste = 0.31,
        Content = 0.12,
        Audio = 0.10,
        Collaborative = 0.20,
        Behavior = 0.20,
        Popularity = 0.04,
        Freshness = 0.03,
    };

    public double Combine(
        double content,
        double? taste,
        double? audio,
        double collaborative,
        double behavior,
        double popularity,
        double freshness,
        double coverage)
    {
        var sum = Content * content
                  + Collaborative * collaborative
                  + Behavior * behavior
                  + Popularity * popularity
                  + Freshness * freshness
                  + Coverage * coverage;

        var present = Content + Collaborative + Behavior + Popularity + Freshness + Coverage;

        if (taste is { } tasteValue)
        {
            sum += Taste * tasteValue;
            present += Taste;
        }

        if (audio is not { } audioValue) return present <= 0 ? 0 : sum / present * Total;
        sum += Audio * audioValue;
        present += Audio;

        return present <= 0 ? 0 : sum / present * Total;
    }
}
