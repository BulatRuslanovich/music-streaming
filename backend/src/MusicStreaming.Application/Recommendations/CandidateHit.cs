// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations;

/// <summary>
/// Трек, названный одним источником, до материализации в <see cref="RecommendationCandidate"/>.
/// </summary>
public record CandidateHit(
    Guid TrackId,
    CandidateSource Source,
    double Content = 0,
    double? AudioSimilarity = null,
    double Collaborative = 0,
    double Popularity = 0,
    string ReasonKind = ReasonKinds.Discovery,
    string? ReasonSubject = null,
    Guid? ReasonSubjectId = null)
{
    public CandidateSourceFamily Families { get; init; } = CandidateSources.FamilyOf(Source);
}

/// <summary>
/// Один независимый способ назвать треки-кандидаты. Источники ничего не знают друг о друге;
/// их результаты сводит <c>CandidateGenerator</c>.
/// </summary>
public interface ICandidateSource
{
    Task<IReadOnlyList<CandidateHit>> FetchAsync(
        UserRecommendationContext context, CancellationToken ct);
}

public static class CandidateHits
{
    /// <summary>
    /// Сведение находок в общий пул. Числовые сигналы берутся по максимуму, а вот источник и
    /// объяснение достаются тому, кто назвал трек первым — поэтому порядок источников значим.
    /// </summary>
    public static void Merge(Dictionary<Guid, CandidateHit> pool, IEnumerable<CandidateHit> produced)
    {
        foreach (var hit in produced)
        {
            if (!pool.TryGetValue(hit.TrackId, out var existing))
            {
                pool[hit.TrackId] = hit;
                continue;
            }

            pool[hit.TrackId] = existing with
            {
                Content = Math.Max(existing.Content, hit.Content),
                AudioSimilarity = Max(existing.AudioSimilarity, hit.AudioSimilarity),
                Collaborative = Math.Max(existing.Collaborative, hit.Collaborative),
                Popularity = Math.Max(existing.Popularity, hit.Popularity),
                Families = existing.Families | hit.Families,
            };
        }
    }

    private static double? Max(double? left, double? right) => (left, right) switch
    {
        (null, null) => null,
        ({ } value, null) => value,
        (null, { } value) => value,
        ({ } leftValue, { } rightValue) => Math.Max(leftValue, rightValue),
    };
}
