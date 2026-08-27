// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations.Sources;

/// <summary>Соседи по схожести для треков, которые человек слушал недавно.</summary>
public class SimilarToRecentSource(TrackNeighbourLookup neighbours) : ICandidateSource
{
    public async Task<IReadOnlyList<CandidateHit>> FetchAsync(
        UserRecommendationContext context, CancellationToken ct)
    {
        var seeds = context.Seeds;
        if (seeds.Count == 0)
            return [];

        return await neighbours.NeighboursOfAsync(seeds, ct);
    }
}
