// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations.Scoring;

public class RankingWeights
{
    public double Content { get; set; }
    public double Audio { get; set; }
    public double Collaborative { get; set; }
    public double Behavior { get; set; }
    public double Popularity { get; set; }
    public double Freshness { get; set; }
    public double Coverage { get; set; }

    public double Total =>
        Content + Audio + Collaborative + Behavior + Popularity + Freshness + Coverage;

    public static RankingWeights ColdDefaults() => new()
    {
        Popularity = 0.40,
        Freshness = 0.25,
        Coverage = 0.35,
    };

    public static RankingWeights WarmDefaults() => new()
    {
        Content = 0.35,
        Audio = 0.10,
        Collaborative = 0.15,
        Behavior = 0.20,
        Popularity = 0.12,
        Freshness = 0.05,
        Coverage = 0.03,
    };

    public static RankingWeights MatureDefaults() => new()
    {
        Content = 0.22,
        Audio = 0.10,
        Collaborative = 0.28,
        Behavior = 0.25,
        Popularity = 0.07,
        Freshness = 0.05,
        Coverage = 0.03,
    };

    public static RankingWeights FlowDefaults() => new()
    {
        Content = 0.40,
        Audio = 0.30,
        Collaborative = 0.20,
        Behavior = 0.08,
        Popularity = 0.01,
        Freshness = 0.01,
    };

    public static RankingWeights DiscoverDefaults() => new()
    {
        Content = 0.25,
        Audio = 0.15,
        Collaborative = 0.18,
        Behavior = 0.25,
        Popularity = 0.03,
        Freshness = 0.10,
        Coverage = 0.04,
    };

    // Кандидат без аудио-фич не должен ни выигрывать, ни проигрывать от самого факта их отсутствия,
    // поэтому вес Audio в таком случае достаётся контентному сигналу.
    public double Combine(
        double content,
        double? audio,
        double collaborative,
        double behavior,
        double popularity,
        double freshness,
        double coverage) =>
        Content * content
        + Audio * (audio ?? content)
        + Collaborative * collaborative
        + Behavior * behavior
        + Popularity * popularity
        + Freshness * freshness
        + Coverage * coverage;
}
