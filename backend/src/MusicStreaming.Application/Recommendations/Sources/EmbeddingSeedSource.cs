// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Recommendations.Embeddings;

namespace MusicStreaming.Application.Recommendations.Sources;

public class EmbeddingSeedSource(
    IApplicationDbContext db,
    IEmbeddingIndex index) : ICandidateSource
{
    private const int SeedCount = 3;

    public async Task<IReadOnlyList<CandidateHit>> FetchAsync(
        UserRecommendationContext context, CancellationToken ct)
    {
        var snapshot = index.Snapshot();
        if (snapshot.IsEmpty || context.Seeds.Count == 0)
            return [];

        var seeds = context.Seeds
            .OrderByDescending(seed => seed.Weight)
            .Where(seed => snapshot.RowOf(seed.TrackId) >= 0)
            .Take(SeedCount)
            .ToList();

        if (seeds.Count == 0)
            return [];

        var perSeed = Math.Max(1, RecommendationTuning.Shelves.PerSourceLimit / seeds.Count);
        var seedIds = seeds.Select(seed => seed.TrackId).ToList();
        var titles = await db.Tracks.AsNoTracking()
            .Where(track => seedIds.Contains(track.Id))
            .ToDictionaryAsync(track => track.Id, track => track.Title, ct);

        var hits = new List<CandidateHit>(perSeed * seeds.Count);

        foreach (var seed in seeds)
        {
            var row = snapshot.RowOf(seed.TrackId);
            var family = snapshot.CloneIds(seed.TrackId).ToHashSet();

            foreach (var neighbour in snapshot.TopK(snapshot.Vector(row), perSeed, family))
            {
                hits.Add(new CandidateHit(
                    neighbour.TrackId,
                    CandidateSource.SonicNeighbour,
                    AudioSimilarity: Math.Max(0, seed.Weight * neighbour.Score),
                    ReasonKind: ReasonKinds.SoundsLike,
                    ReasonSubject: titles.GetValueOrDefault(seed.TrackId),
                    ReasonSubjectId: seed.TrackId));
            }
        }

        return hits;
    }
}
