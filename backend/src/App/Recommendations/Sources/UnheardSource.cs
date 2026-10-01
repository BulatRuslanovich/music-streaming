// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Abstractions;
using Microsoft.EntityFrameworkCore;
using App.Common;

namespace App.Recommendations.Sources;

public class UnheardSource(IApplicationDbContext db)
    : ICandidateSource
{
    public async Task<IReadOnlyList<CandidateHit>> FetchAsync(
        UserRecommendationContext context, CancellationToken ct)
    {
        var userId = context.UserId;

        var trackIds = await db.Tracks.AsNoTracking()
            .Where(t => !db.UserTrackAffinities.Any(a => a.UserId == userId && a.TrackId == t.Id))
            .ByPopularityThenNewest()
            .Take(RecommendationTuning.Shelves.PerSourceLimit)
            .Select(t => t.Id)
            .ToListAsync(ct);

        return
        [
            .. trackIds
                .Select(id => new CandidateHit(
                    id, CandidateSource.Unheard, ReasonKind: ReasonKinds.Discovery))
        ];
    }
}
