// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Globalization;

namespace MusicStreaming.IntegrationTests.Evaluation;

/// <summary>
/// Качество одного ранжированного списка против отложенного окна. Абсолютные числа на синтетике
/// сами по себе ничего не значат — смысл в сравнении двух ранжирований на одних и тех же данных.
/// </summary>
public record RankingQuality(
    string Name,
    int K,
    int Hits,
    int Relevant,
    double Recall,
    double Precision,
    double MeanAveragePrecision,
    double HomeSceneShare)
{
    public string Row() => string.Format(
        CultureInfo.InvariantCulture,
        "{0,-14} recall@{1}={2:F3}  precision={3:F3}  map={4:F3}  scene={5:F3}  ({6}/{7} hits)",
        Name, K, Recall, Precision, MeanAveragePrecision, HomeSceneShare, Hits, Relevant);
}

public static class RecommendationEvaluator
{
    public static RankingQuality Measure(
        string name,
        IReadOnlyList<Guid> ranked,
        IReadOnlySet<Guid> heldOut,
        EvaluationScene home,
        EvaluationCatalog catalog,
        int k)
    {
        var top = ranked.Take(k).ToList();

        var hits = 0;
        var precisionSum = 0.0;

        for (var index = 0; index < top.Count; index++)
        {
            if (!heldOut.Contains(top[index]))
                continue;

            hits++;
            precisionSum += (double)hits / (index + 1);
        }

        var fromHome = top.Count(trackId => catalog.SceneOf(trackId) == home);

        return new RankingQuality(
            name,
            k,
            hits,
            heldOut.Count,
            heldOut.Count == 0 ? 0 : (double)hits / heldOut.Count,
            top.Count == 0 ? 0 : (double)hits / top.Count,
            heldOut.Count == 0 ? 0 : precisionSum / Math.Min(heldOut.Count, k),
            top.Count == 0 ? 0 : (double)fromHome / top.Count);
    }
}
