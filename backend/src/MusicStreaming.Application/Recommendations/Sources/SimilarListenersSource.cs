// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations.Scoring;

namespace MusicStreaming.Application.Recommendations.Sources;

/// <summary>Что слушают те, чьи вкусы пересекаются с этим пользователем.</summary>
public class SimilarListenersSource(IApplicationDbContext db, IOptions<RecommendationOptions> options)
    : ICandidateSource
{
    private const int NeighbourCount = 20;
    private const int MinimumNeighbourOverlap = 3;

    private RecommendationOptions Options => options.Value;

    public async Task<IReadOnlyList<CandidateHit>> FetchAsync(
        UserRecommendationContext context, CancellationToken ct)
    {
        var eligibleUsers = await db.UserTasteProfiles.AsNoTracking()
            .CountAsync(p => p.PositiveSignalCount >= Options.UserCfMinInteractions, ct);

        if (eligibleUsers < Options.UserCfMinUsers)
            return [];

        var liked = await db.UserTrackAffinities.AsNoTracking()
            .Where(a => a.UserId == context.UserId && a.Score > 0)
            .Select(a => a.TrackId)
            .ToListAsync(ct);

        if (liked.Count == 0)
            return [];

        var overlaps = await db.UserTrackAffinities.AsNoTracking()
            .Where(a => a.UserId != context.UserId && a.Score > 0 && liked.Contains(a.TrackId))
            .GroupBy(a => a.UserId)
            .Select(g => new { UserId = g.Key, Overlap = g.Count() })
            .Where(x => x.Overlap >= MinimumNeighbourOverlap)
            .ToListAsync(ct);

        if (overlaps.Count == 0)
            return [];

        var overlapIds = overlaps.Select(row => row.UserId).ToList();
        var sizes = await db.UserTrackAffinities.AsNoTracking()
            .Where(a => overlapIds.Contains(a.UserId) && a.Score > 0)
            .GroupBy(a => a.UserId)
            .Select(group => new { UserId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.UserId, row => row.Count, ct);

        var similarities = overlaps
            .Where(row => sizes.ContainsKey(row.UserId))
            .Select(row => new
            {
                row.UserId,
                Similarity = row.Overlap / Math.Sqrt((double)liked.Count * sizes[row.UserId]),
            })
            .OrderByDescending(row => row.Similarity)
            .Take(NeighbourCount)
            .ToDictionary(row => row.UserId, row => row.Similarity);

        if (similarities.Count == 0)
            return [];

        var neighbours = similarities.Keys.ToList();

        var rows = await db.UserTrackAffinities.AsNoTracking()
            .Where(a => neighbours.Contains(a.UserId) && a.Score > 0.2 && !liked.Contains(a.TrackId))
            .OrderByDescending(a => a.Score)
            .Take(Options.PerSourceLimit * neighbours.Count)
            .Select(a => new { a.UserId, a.TrackId, a.Score })
            .ToListAsync(ct);

        return rows
            .GroupBy(row => row.TrackId)
            .Select(group =>
            {
                var support = group.Select(row => row.UserId).Distinct().Count();
                var totalWeight = group.Sum(row => similarities[row.UserId]);
                var weighted = totalWeight <= 0
                    ? 0
                    : group.Sum(row => similarities[row.UserId] * Math.Clamp(row.Score, 0, 1)) / totalWeight;
                return new CandidateHit(
                    group.Key,
                    CandidateSource.SimilarListeners,
                    Collaborative: AffinityMath.Shrink(weighted, support, Options.CollaborativeShrinkage),
                    ReasonKind: ReasonKinds.PopularWithSimilarTaste);
            })
            .OrderByDescending(hit => hit.Collaborative)
            .Take(Options.PerSourceLimit)
            .ToList();
    }
}
