// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations.Scoring;

namespace MusicStreaming.Application.Recommendations.Sources;

/// <summary>Лучшие треки артистов, к которым человек привязан сильнее всего.</summary>
public class LovedArtistsSource(IApplicationDbContext db, IOptions<RecommendationOptions> options)
    : ICandidateSource
{
    private const int TopArtistCount = 8;

    private RecommendationOptions Options => options.Value;

    public async Task<IReadOnlyList<CandidateHit>> FetchAsync(
        UserRecommendationContext context, CancellationToken ct)
    {
        var artists = SourceQuota.TopScoring(context.Ranking.ArtistScores, TopArtistCount);
        if (artists.Count == 0)
            return [];

        // Для любимого артиста нужны его лучшие треки, а не последние загруженные в библиотеку.
        var rows = await db.Tracks.AsNoTracking()
            .Where(t => t.TrackArtists.Any(ta => artists.Contains(ta.ArtistId)))
            .ByPopularityThenNewest()
            .Take(Options.PerSourceLimit * artists.Count)
            .Select(t => new
            {
                t.Id,
                t.CreatedAt,
                Popularity = t.Stats == null ? 0 : t.Stats.PopularityScore,
                Matches = t.TrackArtists
                    .Where(ta => artists.Contains(ta.ArtistId))
                    .Select(ta => new { ta.ArtistId, ArtistName = ta.Artist!.Name })
                    .ToList(),
            })
            .ToListAsync(ct);

        var strongest = Math.Max(artists.Max(id => context.Ranking.ArtistScores[id]), double.Epsilon);
        var hits = new List<CandidateHit>(Options.PerSourceLimit);

        foreach (var artistId in artists)
        {
            var affinity = Math.Max(0, context.Ranking.ArtistScores[artistId]) / strongest;

            hits.AddRange(rows
                .Where(row => row.Matches.Any(match => match.ArtistId == artistId))
                .OrderByDescending(row => row.Popularity)
                .ThenByDescending(row => row.CreatedAt)
                .Take(SourceQuota.Of(Options.PerSourceLimit, affinity, artists.Count))
                .Select(row =>
                {
                    var match = row.Matches.First(item => item.ArtistId == artistId);
                    return new CandidateHit(
                        row.Id, CandidateSource.LovedArtists, Content: 0.45 + 0.35 * affinity,
                        ReasonKind: ReasonKinds.BecauseYouListened,
                        ReasonSubject: match.ArtistName, ReasonSubjectId: artistId);
                }));
        }

        return hits;
    }
}
