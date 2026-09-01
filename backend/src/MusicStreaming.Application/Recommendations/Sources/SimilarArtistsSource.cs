// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations.Scoring;

namespace MusicStreaming.Application.Recommendations.Sources;

/// <summary>
/// Артисты, похожие по тег-вектору на тех, кого пользователь слушает. Единственный источник,
/// который умеет выйти за пределы истории и жанрового ярлыка — теги приходят из внешнего
/// каталога, а не из этой библиотеки.
/// </summary>
public class SimilarArtistsSource(IApplicationDbContext db, IOptions<RecommendationOptions> options)
    : ICandidateSource
{
    private const int TopArtistCount = 8;
    private const int TasteTagCount = 24;
    private const int NeighbourArtistCount = 12;
    private const int NeighbourArtistPoolSize = 60;
    private const double MinimumArtistTagSimilarity = 0.15;

    private RecommendationOptions Options => options.Value;

    public async Task<IReadOnlyList<CandidateHit>> FetchAsync(
        UserRecommendationContext context, CancellationToken ct)
    {
        var loved = SourceQuota.TopScoring(context.Ranking.ArtistScores, TopArtistCount);
        if (loved.Count == 0)
            return [];

        var lovedTags = await db.ArtistTags.AsNoTracking()
            .Where(tag => loved.Contains(tag.ArtistId))
            .Select(tag => new { tag.ArtistId, tag.Name, tag.Weight })
            .ToListAsync(ct);

        if (lovedTags.Count == 0)
            return [];

        var strongest = Math.Max(loved.Max(id => context.Ranking.ArtistScores[id]), double.Epsilon);

        // Профиль вкуса в тегах: вес тега умножается на то, насколько силён давший его артист.
        var taste = lovedTags
            .GroupBy(tag => tag.Name, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(tag =>
                    tag.Weight * Math.Max(0, context.Ranking.ArtistScores[tag.ArtistId]) / strongest),
                StringComparer.Ordinal);

        var wanted = taste
            .OrderByDescending(pair => pair.Value)
            .Take(TasteTagCount)
            .Select(pair => pair.Key)
            .ToList();

        var tasteNorm = Math.Sqrt(wanted.Sum(name => taste[name] * taste[name]));
        if (tasteNorm <= 0)
            return [];

        // Сначала кто вообще пересекается по тегам, и только потом их векторы целиком: иначе
        // усечение могло бы обрезать артиста на середине вектора и испортить его норму.
        var pool = await db.ArtistTags.AsNoTracking()
            .Where(tag => !loved.Contains(tag.ArtistId) && wanted.Contains(tag.Name))
            .GroupBy(tag => tag.ArtistId)
            .OrderByDescending(group => group.Sum(tag => tag.Weight))
            .Take(NeighbourArtistPoolSize)
            .Select(group => group.Key)
            .ToListAsync(ct);

        if (pool.Count == 0)
            return [];

        var neighbourTags = await db.ArtistTags.AsNoTracking()
            .Where(tag => pool.Contains(tag.ArtistId))
            .Select(tag => new { tag.ArtistId, tag.Name, tag.Weight, ArtistName = tag.Artist!.Name })
            .ToListAsync(ct);

        var neighbours = neighbourTags
            .GroupBy(tag => tag.ArtistId)
            .Select(group =>
            {
                var norm = Math.Sqrt(group.Sum(tag => tag.Weight * tag.Weight));
                var dot = group.Sum(tag => taste.TryGetValue(tag.Name, out var weight)
                    ? weight * tag.Weight
                    : 0);

                return new
                {
                    ArtistId = group.Key,
                    ArtistName = group.First().ArtistName,
                    Similarity = norm <= 0 ? 0 : dot / (norm * tasteNorm),
                };
            })
            .Where(row => row.Similarity >= MinimumArtistTagSimilarity)
            .OrderByDescending(row => row.Similarity)
            .Take(NeighbourArtistCount)
            .ToList();

        if (neighbours.Count == 0)
            return [];

        var closest = neighbours.Select(row => row.ArtistId).ToList();

        var rows = await db.Tracks.AsNoTracking()
            .Where(t => t.TrackArtists.Any(ta => closest.Contains(ta.ArtistId)))
            .OrderByDescending(t => t.Stats == null ? 0 : t.Stats.PopularityScore)
            .ThenByDescending(t => t.CreatedAt)
            .Take(Options.PerSourceLimit * neighbours.Count)
            .Select(t => new
            {
                t.Id,
                t.CreatedAt,
                Popularity = t.Stats == null ? 0 : t.Stats.PopularityScore,
                Credits = t.TrackArtists.Select(ta => ta.ArtistId).ToList(),
            })
            .ToListAsync(ct);

        var quota = Math.Max(1, (int)Math.Ceiling((double)Options.PerSourceLimit / neighbours.Count));
        var hits = new List<CandidateHit>(Options.PerSourceLimit);

        foreach (var neighbour in neighbours)
        {
            hits.AddRange(rows
                .Where(row => row.Credits.Contains(neighbour.ArtistId))
                .OrderByDescending(row => row.Popularity)
                .ThenByDescending(row => row.CreatedAt)
                .Take(quota)
                .Select(row => new CandidateHit(
                    row.Id,
                    CandidateSource.SimilarArtists,
                    Content: 0.35 + 0.45 * neighbour.Similarity,
                    ReasonKind: ReasonKinds.SimilarTo,
                    ReasonSubject: neighbour.ArtistName,
                    ReasonSubjectId: neighbour.ArtistId)));
        }

        return hits;
    }
}
