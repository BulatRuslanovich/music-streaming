// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;

namespace MusicStreaming.Application.Recommendations.Sources;

/// <summary>Что в библиотеке есть, но этот человек ещё не слышал.</summary>
public class UnheardSource(IApplicationDbContext db, IOptions<RecommendationOptions> options)
    : ICandidateSource
{
    private RecommendationOptions Options => options.Value;

    public async Task<IReadOnlyList<CandidateHit>> FetchAsync(
        UserRecommendationContext context, CancellationToken ct)
    {
        var userId = context.UserId;

        // Непрослушанное сортируем по тому, как его принимает библиотека, а не по дате импорта:
        // свежие поступления и так покрыты источником NewReleases.
        var trackIds = await db.Tracks.AsNoTracking()
            .Where(t => !db.UserTrackAffinities.Any(a => a.UserId == userId && a.TrackId == t.Id))
            .ByPopularityThenNewest()
            .Take(Options.PerSourceLimit)
            .Select(t => t.Id)
            .ToListAsync(ct);

        return trackIds
            .Select(id => new CandidateHit(
                id, CandidateSource.Unheard, ReasonKind: ReasonKinds.Discovery))
            .ToList();
    }
}
