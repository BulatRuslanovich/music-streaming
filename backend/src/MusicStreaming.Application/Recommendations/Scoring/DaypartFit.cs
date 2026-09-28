// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Recommendations.Scoring;

/// <summary>
/// Насколько кандидат похож на то, что человек слушает в эту часть суток: по жанрам.
/// Сигнал слабый по природе — вечер это не жанр, — поэтому наружу отдаётся 0..1, а насколько сильно
/// его слушать, решает полка.
/// </summary>
public static class DaypartFit
{
    /// <summary>Ни за, ни против: возвращается, когда судить не по чему.</summary>
    private const double Neutral = 0.5;

    public static double For(RecommendationCandidate candidate, DaypartTaste taste)
    {
        if (taste.TopGenres.Count == 0)
            return Neutral;

        if (candidate.GenreId is not { } genreId)
            return 0;

        var strongest = taste.TopGenres.Max(entry => entry.Score);
        if (strongest <= 0)
            return Neutral;

        var match = taste.TopGenres.FirstOrDefault(entry => entry.Id == genreId);

        return match is null ? 0 : Math.Clamp(match.Score / strongest, 0, 1);
    }
}
