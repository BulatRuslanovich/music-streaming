// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations.Embeddings;
using MusicStreaming.Application.Services.Recommendations;

namespace MusicStreaming.Application.Recommendations.Sources;

/// <summary>
/// Треки, близкие к вектору вкуса целиком — без опоры на артиста, жанр или чужие прослушивания.
/// <para>
/// Единственный источник, который может назвать трек, о котором в базе не известно ровно ничего:
/// ни тегов, ни кредитов, ни истории. Его подпись при этом самая слабая из всех
/// («подходит вашему вкусу» не объясняет ничего), поэтому в порядке опроса он стоит почти
/// последним: объяснение достаётся тому, кто назвал трек первым, и пусть это будет кто-то,
/// кому есть что сказать.
/// </para>
/// </summary>
public class EmbeddingTasteSource(
    IEmbeddingIndex index,
    TasteVectorReader tasteVectors,
    IOptions<RecommendationOptions> options) : ICandidateSource
{
    private RecommendationOptions Options => options.Value;

    public async Task<IReadOnlyList<CandidateHit>> FetchAsync(
        UserRecommendationContext context, CancellationToken ct)
    {
        var snapshot = index.Snapshot();
        if (snapshot.IsEmpty)
            return [];

        var taste = await tasteVectors.CurrentAsync(context.UserId, context.Ranking.Now, snapshot, ct);
        if (!taste.IsReady)
            return [];

        var hits = snapshot.TopK(taste.Query, Options.Shelves.PerSourceLimit, context.SuppressedTracks);

        return [.. hits.Select(hit => new CandidateHit(
            hit.TrackId,
            CandidateSource.TasteVector,
            Taste: Math.Max(0, hit.Score),
            ReasonKind: ReasonKinds.MatchesYourTaste))];
    }
}
