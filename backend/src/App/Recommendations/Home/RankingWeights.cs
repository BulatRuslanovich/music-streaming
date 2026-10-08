// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Domain.Entities.Recommendations;

namespace App.Recommendations.Home;

// Признаки кандидата, из которых складывается его достоинство, — в одном порядке для весов, кеша полок
// и обучения персональных весов.
public static class RankingFeatures
{
    public static readonly string[] Names =
        ["taste", "content", "audio", "collaborative", "behavior", "popularity", "freshness", "coverage"];

    public static int Count => Names.Length;

    public static double?[] Of(RecommendationCandidate candidate) =>
    [
        candidate.TasteFit,
        candidate.Content,
        candidate.AudioSimilarity,
        candidate.Collaborative,
        candidate.Behavior,
        candidate.Popularity,
        candidate.Freshness,
        candidate.Coverage,
    ];
}

// Веса признаков, в сумме единица.
public sealed record RankingWeights(double[] Values)
{
    // Ручные веса по зрелости профиля: холодный ранжируется почти без личных сигналов — по популярности,
    // свежести и звучанию первых затравок; с опытом растёт вес вкуса, поведения и плейлистов.
    public static RankingWeights Hand(ProfileMaturity maturity) => maturity switch
    {
        ProfileMaturity.Mature => new([0.31, 0.12, 0.10, 0.20, 0.20, 0.04, 0.03, 0.0]),
        ProfileMaturity.Warm => new([0.26, 0.22, 0.10, 0.13, 0.17, 0.08, 0.04, 0.0]),
        _ => new([0.15, 0.0, 0.05, 0.0, 0.0, 0.30, 0.20, 0.30]),
    };

    public double this[int feature] => Values[feature];
}
