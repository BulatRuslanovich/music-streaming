// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Abstractions;
using App.Recommendations.Scoring;
using Microsoft.EntityFrameworkCore;
using App.Common;

namespace App.Recommendations.Sources;

public class LovedGenresSource(IApplicationDbContext db)
    : ICandidateSource
{
    private const int TopGenreCount = 4;

    public async Task<IReadOnlyList<CandidateHit>> FetchAsync(
        UserRecommendationContext context, CancellationToken ct)
    {
        var genres = SourceQuota.TopScoring(context.Ranking.GenreScores, TopGenreCount);
        if (genres.Count == 0)
            return [];

        var rows = await db.Tracks.AsNoTracking()
            .Where(t => t.GenreId != null && genres.Contains(t.GenreId.Value))
            .ByPopularityThenNewest()
            .Take(RecommendationTuning.Shelves.PerSourceLimit * genres.Count)
            .Select(t => new { t.Id, t.GenreId, GenreName = t.Genre!.Name })
            .ToListAsync(ct);

        var strongest = Math.Max(genres.Max(id => context.Ranking.GenreScores[id]), double.Epsilon);

        return genres
            .SelectMany(genreId => rows
                .Where(row => row.GenreId == genreId)
                .Take(SourceQuota.Of(
                    RecommendationTuning.Shelves.PerSourceLimit,
                    Math.Max(0, context.Ranking.GenreScores[genreId]) / strongest,
                    genres.Count))
                .Select(row => new CandidateHit(
                    row.Id,
                    CandidateSource.LovedGenres,
                    Content: 0.25 + 0.35 * Math.Max(0, context.Ranking.GenreScores[genreId]) / strongest,
                    ReasonKind: ReasonKinds.FromGenreYouLike,
                    ReasonSubject: row.GenreName,
                    ReasonSubjectId: row.GenreId)))
            .ToList();
    }
}
