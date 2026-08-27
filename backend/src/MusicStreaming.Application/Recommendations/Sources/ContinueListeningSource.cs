// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;

namespace MusicStreaming.Application.Recommendations.Sources;

/// <summary>Треки, которые начали, но не дослушали и не пропустили.</summary>
public class ContinueListeningSource(IApplicationDbContext db, IOptions<RecommendationOptions> options)
    : ICandidateSource
{
    private RecommendationOptions Options => options.Value;

    public async Task<IReadOnlyList<CandidateHit>> FetchAsync(
        UserRecommendationContext context, CancellationToken ct)
    {
        var since = context.Ranking.Now.AddDays(-Options.RecentlyPlayedDays);

        var trackIds = await db.UserTrackAffinities.AsNoTracking()
            .Where(a => a.UserId == context.UserId
                        && a.LastPlayedAt >= since
                        && a.CompletionSamples > 0
                        && a.CompletedCount == 0
                        && a.SkipCount == 0)
            .OrderByDescending(a => a.LastPlayedAt)
            .Take(Options.ShelfSize * 2)
            .Select(a => a.TrackId)
            .ToListAsync(ct);

        return trackIds
            .Select(id => new CandidateHit(id, CandidateSource.ContinueListening,
                Content: 1, ReasonKind: ReasonKinds.ContinueListening))
            .ToList();
    }
}
