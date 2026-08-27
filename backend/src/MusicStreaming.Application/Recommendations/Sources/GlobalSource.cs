// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;

namespace MusicStreaming.Application.Recommendations.Sources;

/// <summary>
/// Общебиблиотечные источники — свежее и популярное. Единственный, который что-то даёт при
/// холодном старте, поэтому радио вокруг трека тоже добирает из него, когда пул слишком мал.
/// </summary>
public class GlobalSource(IApplicationDbContext db, IOptions<RecommendationOptions> options)
    : ICandidateSource
{
    private RecommendationOptions Options => options.Value;

    public async Task<IReadOnlyList<CandidateHit>> FetchAsync(
        UserRecommendationContext context, CancellationToken ct)
    {
        var fresh = db.Tracks.AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .Take(Options.PerSourceLimit)
            .Select(t => new
            {
                TrackId = t.Id,
                Source = CandidateSource.NewReleases,
                t.ArtistId,
                ArtistName = t.Artist!.Name,
                Popularity = 0d,
            });

        var popular = db.TrackStats.AsNoTracking()
            .Where(s => s.PopularityScore > 0)
            .OrderByDescending(s => s.PopularityScore)
            .Take(Options.PerSourceLimit)
            .Select(s => new
            {
                TrackId = s.TrackId,
                Source = CandidateSource.Popular,
                ArtistId = s.Track!.ArtistId,
                ArtistName = s.Track.Artist!.Name,
                Popularity = s.PopularityScore,
            });

        var rows = await fresh.Concat(popular).ToListAsync(ct);

        return rows.Select(row =>
        {
            if (row.Source == CandidateSource.Popular)
            {
                return new CandidateHit(
                    row.TrackId,
                    CandidateSource.Popular,
                    Popularity: row.Popularity,
                    ReasonKind: ReasonKinds.Trending);
            }

            var artistId = row.ArtistId;
            var known = context.Ranking.ArtistScores.TryGetValue(artistId, out var score) && score > 0;

            return known
                ? new CandidateHit(row.TrackId, CandidateSource.NewReleases,
                    ReasonKind: ReasonKinds.NewFromArtistYouPlay,
                    ReasonSubject: row.ArtistName, ReasonSubjectId: artistId)
                : new CandidateHit(row.TrackId, CandidateSource.NewReleases,
                    ReasonKind: ReasonKinds.FreshInLibrary);
        }).ToList();
    }
}
