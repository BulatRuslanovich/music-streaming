// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Recommendations.Scoring;

/// <summary>
/// Насколько кандидат похож на то, что человек слушает в эту часть суток: по жанрам и по энергии.
/// Сигнал слабый по природе — вечер это не жанр, — поэтому наружу отдаётся 0..1, а насколько сильно
/// его слушать, решает полка.
/// </summary>
public static class DaypartFit
{
    private const double Neutral = 0.5;
    private const double EnergyTolerance = 0.25;

    public static double For(RecommendationCandidate candidate, DaypartTaste taste)
    {
        var genre = GenreFit(candidate, taste);
        var energy = EnergyFit(candidate, taste);

        return (genre, energy) switch
        {
            (null, null) => Neutral,
            ({ } only, null) => only,
            (null, { } only) => only,
            ({ } left, { } right) => 0.6 * left + 0.4 * right,
        };
    }

    private static double? GenreFit(RecommendationCandidate candidate, DaypartTaste taste)
    {
        if (taste.TopGenres.Count == 0)
            return null;

        if (candidate.GenreId is not { } genreId)
            return 0;

        var strongest = taste.TopGenres.Max(entry => entry.Score);
        if (strongest <= 0)
            return null;

        var match = taste.TopGenres.FirstOrDefault(entry => entry.Id == genreId);

        return match is null ? 0 : Math.Clamp(match.Score / strongest, 0, 1);
    }

    private static double? EnergyFit(RecommendationCandidate candidate, DaypartTaste taste)
    {
        if (taste.Energy is not { } wanted || candidate.AudioProfile is not { } profile)
            return null;

        return Math.Exp(-Math.Abs(profile.Energy - wanted) / EnergyTolerance);
    }
}
