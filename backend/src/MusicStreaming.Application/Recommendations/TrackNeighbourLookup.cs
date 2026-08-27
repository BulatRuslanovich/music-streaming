// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations.Scoring;

namespace MusicStreaming.Application.Recommendations;

/// <summary>
/// Соседи трека по <c>track_similarity</c> и запасной путь, когда их ещё не посчитали.
/// Нужен трём разным местам — источнику SimilarToRecent, радио вокруг трека и выдаче похожих, —
/// поэтому живёт отдельно от них всех.
/// </summary>
public class TrackNeighbourLookup(IApplicationDbContext db, IOptions<RecommendationOptions> options)
{
    private RecommendationOptions Options => options.Value;

    public async Task<List<CandidateHit>> NeighboursOfAsync(
        IReadOnlyList<RecommendationSeed> seeds, CancellationToken ct)
    {
        if (seeds.Count == 0)
            return [];

        var seedIds = seeds.Select(seed => seed.TrackId).ToList();
        var rows = await db.TrackSimilarities.AsNoTracking()
            .Where(s => seedIds.Contains(s.TrackId))
            .Select(s => new
            {
                s.TrackId,
                s.SimilarTrackId,
                s.Score,
                s.ContentScore,
                s.AudioScore,
                s.CollabScore,
                SeedTitle = s.Track!.Title,
                SeedArtist = s.Track.Artist!.Name,
                SeedArtistId = s.Track.ArtistId,
            })
            .ToListAsync(ct);

        var strongest = Math.Max(seeds.Max(seed => seed.Weight), double.Epsilon);
        var perSeed = Math.Max(6, Options.PerSourceLimit / seeds.Count);
        var weights = seeds.ToDictionary(seed => seed.TrackId, seed => seed.Weight / strongest);
        var hits = new Dictionary<Guid, CandidateHit>();

        foreach (var row in rows
                     .GroupBy(row => row.TrackId)
                     .SelectMany(group => group.OrderByDescending(row => row.Score).Take(perSeed))
                     .OrderByDescending(row => row.Score * weights[row.TrackId]))
        {
            var weight = weights[row.TrackId];
            var collaborative = row.CollabScore > row.ContentScore;
            CandidateHits.Merge(hits, [new CandidateHit(
                row.SimilarTrackId,
                CandidateSource.SimilarToRecent,
                row.ContentScore * weight,
                row.AudioScore * weight,
                row.CollabScore * weight,
                ReasonKind: collaborative ? ReasonKinds.SimilarTo : ReasonKinds.BecauseYouListened,
                ReasonSubject: collaborative ? row.SeedTitle : row.SeedArtist,
                ReasonSubjectId: collaborative ? row.TrackId : row.SeedArtistId)]);
        }

        return hits.Values.ToList();
    }

    public async Task<IReadOnlyList<Guid>> SameArtistOrGenreAsync(
        Guid seedTrackId, int limit, CancellationToken ct = default)
    {
        var seed = await db.Tracks.AsNoTracking()
            .Where(t => t.Id == seedTrackId)
            .Select(t => new { t.ArtistId, t.GenreId })
            .FirstOrDefaultAsync(ct);

        if (seed is null)
            return [];

        return await db.Tracks.AsNoTracking()
            .Where(t => t.Id != seedTrackId
                        && (t.TrackArtists.Any(ta => ta.ArtistId == seed.ArtistId)
                            || (seed.GenreId != null && t.GenreId == seed.GenreId)))
            .OrderByDescending(t => t.ArtistId == seed.ArtistId)
            .ThenByDescending(t => t.CreatedAt)
            .Take(limit)
            .Select(t => t.Id)
            .ToListAsync(ct);
    }
}
