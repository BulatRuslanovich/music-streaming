// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;

namespace MusicStreaming.Application.Recommendations.Sources;

public class SharedPlaylistsSource(IApplicationDbContext db)
    : ICandidateSource
{
    private const int NeighbourCount = 20;

    public async Task<IReadOnlyList<CandidateHit>> FetchAsync(
        UserRecommendationContext context, CancellationToken ct)
    {
        var seeds = context.SeedTrackIds;
        if (seeds.Count == 0)
            return [];

        var playlistIds = await db.PlaylistTracks.AsNoTracking()
            .Where(pt => seeds.Contains(pt.TrackId))
            .Select(pt => pt.PlaylistId)
            .Distinct()
            .Take(NeighbourCount)
            .ToListAsync(ct);

        if (playlistIds.Count == 0)
            return [];

        var rows = await db.PlaylistTracks.AsNoTracking()
            .Where(pt => playlistIds.Contains(pt.PlaylistId) && !seeds.Contains(pt.TrackId))
            .GroupBy(pt => pt.TrackId)
            .Select(group => new { TrackId = group.Key, Support = group.Count() })
            .OrderByDescending(row => row.Support)
            .Take(RecommendationTuning.Shelves.PerSourceLimit)
            .ToListAsync(ct);

        return rows.Select(row => new CandidateHit(
            row.TrackId, CandidateSource.SharedPlaylists,
            Collaborative: 0.35 + 0.15 * Math.Min(1, row.Support / 3.0),
            ReasonKind: ReasonKinds.PopularWithSimilarTaste)).ToList();
    }
}
