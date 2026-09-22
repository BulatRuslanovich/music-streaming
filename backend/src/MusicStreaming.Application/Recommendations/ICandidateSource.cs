// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations;

/// <summary>One independent way of naming candidate tracks.</summary>
/// <remarks>
/// Источники ничего не знают друг о друге; их результаты сводит <c>CandidateGenerator</c>,
/// и порядок их регистрации в <c>AddCandidateSources</c> определяет, чья подпись победит.
/// </remarks>
public interface ICandidateSource
{
    Task<IReadOnlyList<CandidateHit>> FetchAsync(
        UserRecommendationContext context, CancellationToken ct);
}
