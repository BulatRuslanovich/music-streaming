// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Recommendations.Embeddings;
using MusicStreaming.Application.Services.Recommendations;

namespace MusicStreaming.Application.Recommendations.Sources;

public class EmbeddingTasteSource(
    IEmbeddingIndex index,
    TasteVectorReader tasteVectors) : ICandidateSource
{
    public async Task<IReadOnlyList<CandidateHit>> FetchAsync(
        UserRecommendationContext context, CancellationToken ct)
    {
        var snapshot = index.Snapshot();
        if (snapshot.IsEmpty)
            return [];

        var taste = await tasteVectors.CurrentAsync(context.UserId, snapshot, ct);
        if (!taste.IsReady)
            return [];

        var hits = snapshot.TopK(taste.Query, RecommendationTuning.Shelves.PerSourceLimit);

        return [.. hits.Select(hit => new CandidateHit(
            hit.TrackId,
            CandidateSource.TasteVector,
            Taste: Math.Max(0, hit.Score),
            ReasonKind: ReasonKinds.MatchesYourTaste))];
    }
}
