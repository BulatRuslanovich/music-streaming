// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Domain.Entities.Recommendations;

namespace App.Recommendations;

public static class EventWeights
{
    public static double CompletionRatio(int listenedSeconds, int durationSeconds)
    {
        if (durationSeconds <= 0 || listenedSeconds <= 0)
            return 0;

        return Math.Min(1.0, (double)listenedSeconds / durationSeconds);
    }

    // Насколько верить отказу (отрицательному весу скипа) с учётом того, откуда запущен трек:
    // брошенная рекомендация — явное «не то», скип при листании своего альбома — чаще просто «дальше».
    public const double RecommendationSkipFactor = 1.5;

    public const double BrowsingSkipFactor = 0.5;

    public static double SkipFactor(string? source) =>
        PlaybackSource.IsRecommendation(source) ? RecommendationSkipFactor
        : PlaybackSource.IsBrowsing(source) ? BrowsingSkipFactor
        : 1.0;

    public static double WithSkipSource(PlaybackEventType type, double weight, string? source) =>
        type == PlaybackEventType.TrackSkipped && weight < 0 ? weight * SkipFactor(source) : weight;
}
