// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations.Scoring;

public class RankingWeights
{
    public double Content { get; set; }
    public double Collaborative { get; set; }
    public double Behavior { get; set; }
    public double Popularity { get; set; }
    public double Freshness { get; set; }
    public double Coverage { get; set; }
    public double Total => Content + Collaborative + Behavior + Popularity + Freshness + Coverage;
    public static RankingWeights ColdDefaults() => new()
    {
        Popularity = 0.40,
        Freshness = 0.25,
        Coverage = 0.35,
    };

    public static RankingWeights WarmDefaults() => new()
    {
        Content = 0.40,
        Collaborative = 0.15,
        Behavior = 0.20,
        Popularity = 0.15,
        Freshness = 0.10,
    };

    public static RankingWeights MatureDefaults() => new()
    {
        Content = 0.25,
        Collaborative = 0.30,
        Behavior = 0.25,
        Popularity = 0.10,
        Freshness = 0.10,
    };

    public static RankingWeights FlowDefaults() => new()
    {
        Content = 0.50,
        Collaborative = 0.25,
        Behavior = 0.20,
        Popularity = 0.03,
        Freshness = 0.02,
    };

    public static RankingWeights DiscoverDefaults() => new()
    {
        Content = 0.30,
        Collaborative = 0.20,
        Behavior = 0.30,
        Popularity = 0.05,
        Freshness = 0.15,
    };

    public double Combine(
        double content,
        double collaborative,
        double behavior,
        double popularity,
        double freshness,
        double coverage) =>
        Content * content
        + Collaborative * collaborative
        + Behavior * behavior
        + Popularity * popularity
        + Freshness * freshness
        + Coverage * coverage;
}
