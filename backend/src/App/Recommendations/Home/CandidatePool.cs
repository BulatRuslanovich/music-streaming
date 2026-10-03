// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Abstractions;
using App.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Domain.Entities.Recommendations;
using App.Recommendations.Embeddings;

namespace App.Recommendations.Home;

public class CandidatePool(
    IApplicationDbContext db,
    EmbeddingIndex embeddingIndex,
    TasteVectorReader tasteVectors,
    IMemoryCache memoryCache,
    ILogger<CandidatePool> logger)
{
    private const int SeedTrackCount = 20;
    private static readonly TimeSpan SeedRecencyHalfLife = TimeSpan.FromDays(30);
    private const int SonicSeedCount = 3;
    private const int TopArtistCount = 8;
    private const int TopGenreCount = 4;
    private const int PlaylistNeighbourCount = 20;
    private const string GenreShareCacheKey = "recommendations:genre-share";
    private static readonly TimeSpan GenreShareLifetime = TimeSpan.FromMinutes(5);

    public async Task<(UserRecommendationContext Context, List<RecommendationCandidate> Candidates)> LoadAsync(
        Guid userId, DateTimeOffset now, CancellationToken ct = default)
    {
        var profile = await db.UserTasteProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct)
            ?? new UserTasteProfile { UserId = userId };

        var artistScores = await db.UserArtistAffinities.AsNoTracking()
            .Where(a => a.UserId == userId)
            .ToDictionaryAsync(a => a.ArtistId, a => a.Score, ct);

        var genreScores = await db.UserGenreAffinities.AsNoTracking()
            .Where(a => a.UserId == userId)
            .ToDictionaryAsync(a => a.GenreId, a => a.Score, ct);

        var history = (await db.UserTrackAffinities.AsNoTracking()
                .Where(a => a.UserId == userId)
                .Select(a => new
                {
                    a.TrackId,
                    a.LastPlayedAt,
                    a.PlayCount,
                    a.CompletedCount,
                    a.SkipCount,
                    a.ReplayCount,
                    a.PlaylistAdds,
                    a.CompletionSum,
                    a.CompletionSamples,
                    a.Score,
                })
                .ToListAsync(ct))
            .ToDictionary(
                h => h.TrackId,
                h => new TrackHistory(
                    h.LastPlayedAt,
                    h.PlayCount,
                    h.SkipCount,
                    h.CompletionSamples == 0 ? 0 : h.CompletionSum / h.CompletionSamples,
                    h.Score,
                    h.CompletedCount,
                    h.ReplayCount,
                    h.PlaylistAdds));

        var ranking = new RankingContext(artistScores, genreScores, history, now, profile.YearCenter, profile.YearSpread);

        // Затравки: недавние треки, которые реально зашли (дослушаны, переиграны, добавлены в плейлист).
        var seeds = new List<RecommendationSeed>();

        foreach (var (trackId, track) in history)
        {
            if (track.Score <= 0 || (track.SkipCount >= 2 && track.AverageCompletion < 0.20 && track.Score < 0.35))
                continue;

            var engagement = Math.Max(
                Math.Clamp(track.AverageCompletion, 0, 1),
                Math.Max(
                    track.CompletedCount > 0 ? 0.85 : 0,
                    Math.Max(track.ReplayCount > 0 ? 0.95 : 0, track.PlaylistAdds > 0 ? 1 : 0)));

            engagement = Math.Max(engagement, Math.Clamp(track.Score * 2, 0, 1));

            var repetition = 1 - Math.Exp(-Math.Max(1, track.PlayCount) / 3.0);
            var age = Math.Max(0, (now - track.LastPlayedAt).TotalSeconds);
            var recency = Math.Pow(0.5, age / SeedRecencyHalfLife.TotalSeconds);

            var weight = track.Score
                         * (0.35 + 0.45 * engagement + 0.20 * repetition)
                         * (0.45 + 0.55 * recency);

            if (weight > 0)
                seeds.Add(new RecommendationSeed(trackId, weight));
        }

        seeds = [.. seeds.OrderByDescending(seed => seed.Weight).ThenBy(seed => seed.TrackId).Take(SeedTrackCount)];

        var genreShare = await memoryCache.GetOrCreateAsync(GenreShareCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = GenreShareLifetime;

            var counts = await db.Tracks.AsNoTracking()
                .Where(t => t.GenreId != null)
                .GroupBy(t => t.GenreId!.Value)
                .Select(g => new { GenreId = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var total = counts.Sum(c => c.Count);
            return total == 0
                ? new Dictionary<Guid, double>()
                : counts.ToDictionary(c => c.GenreId, c => (double)c.Count / total);
        });

        var snapshot = embeddingIndex.Snapshot();
        var taste = await tasteVectors.CurrentAsync(userId, snapshot, ct);

        var context = new UserRecommendationContext(userId, profile, ranking);

        // Источники кандидатов. Порядок важен: при совпадении трека причину (reason) задаёт первый.
        var hits = new Dictionary<Guid, CandidateHit>();

        // Похожие по звучанию на самые сильные затравки.
        var sonicSeeds = snapshot.IsEmpty
            ? []
            : seeds.Where(seed => snapshot.RowOf(seed.TrackId) >= 0).Take(SonicSeedCount).ToList();

        if (sonicSeeds.Count > 0)
        {
            var perSeed = Math.Max(1, RecommendationTuning.Shelves.PerSourceLimit / sonicSeeds.Count);
            var sonicSeedIds = sonicSeeds.Select(seed => seed.TrackId).ToList();
            var titles = await db.Tracks.AsNoTracking()
                .Where(track => sonicSeedIds.Contains(track.Id))
                .ToDictionaryAsync(track => track.Id, track => track.Title, ct);

            var sonic = new List<CandidateHit>(perSeed * sonicSeeds.Count);

            foreach (var seed in sonicSeeds)
            {
                var family = snapshot.CloneIds(seed.TrackId).ToHashSet();

                foreach (var neighbour in snapshot.TopK(snapshot.Vector(snapshot.RowOf(seed.TrackId)), perSeed, family))
                {
                    sonic.Add(new CandidateHit(
                        neighbour.TrackId,
                        CandidateSource.SonicNeighbour,
                        AudioSimilarity: Math.Max(0, seed.Weight * neighbour.Score),
                        ReasonKind: ReasonKinds.SoundsLike,
                        ReasonSubject: titles.GetValueOrDefault(seed.TrackId),
                        ReasonSubjectId: seed.TrackId));
                }
            }

            CandidateHit.Merge(hits, sonic);
        }

        // Треки любимых артистов: квота на артиста растёт с его affinity.
        var lovedArtists = TopScoring(artistScores, TopArtistCount);
        if (lovedArtists.Count > 0)
        {
            var rows = await db.Tracks.AsNoTracking()
                .Where(t => t.TrackArtists.Any(ta => lovedArtists.Contains(ta.ArtistId)))
                .ByPopularityThenNewest()
                .Take(RecommendationTuning.Shelves.PerSourceLimit * lovedArtists.Count)
                .Select(t => new
                {
                    t.Id,
                    t.CreatedAt,
                    Popularity = t.Stats == null ? 0 : t.Stats.PopularityScore,
                    Matches = t.TrackArtists
                        .Where(ta => lovedArtists.Contains(ta.ArtistId))
                        .Select(ta => new { ta.ArtistId, ArtistName = ta.Artist!.Name })
                        .ToList(),
                })
                .ToListAsync(ct);

            var strongest = Math.Max(lovedArtists.Max(id => artistScores[id]), double.Epsilon);
            var byArtist = new List<CandidateHit>(RecommendationTuning.Shelves.PerSourceLimit);

            foreach (var artistId in lovedArtists)
            {
                var affinity = Math.Max(0, artistScores[artistId]) / strongest;

                byArtist.AddRange(rows
                    .Where(row => row.Matches.Any(match => match.ArtistId == artistId))
                    .OrderByDescending(row => row.Popularity)
                    .ThenByDescending(row => row.CreatedAt)
                    .Take(QuotaOf(RecommendationTuning.Shelves.PerSourceLimit, affinity, lovedArtists.Count))
                    .Select(row => new CandidateHit(
                        row.Id, CandidateSource.LovedArtists, Content: 0.45 + 0.35 * affinity,
                        ReasonKind: ReasonKinds.BecauseYouListened,
                        ReasonSubject: row.Matches.First(item => item.ArtistId == artistId).ArtistName,
                        ReasonSubjectId: artistId)));
            }

            CandidateHit.Merge(hits, byArtist);
        }

        // Треки любимых жанров.
        var lovedGenres = TopScoring(genreScores, TopGenreCount);
        if (lovedGenres.Count > 0)
        {
            var rows = await db.Tracks.AsNoTracking()
                .Where(t => t.GenreId != null && lovedGenres.Contains(t.GenreId.Value))
                .ByPopularityThenNewest()
                .Take(RecommendationTuning.Shelves.PerSourceLimit * lovedGenres.Count)
                .Select(t => new { t.Id, t.GenreId, GenreName = t.Genre!.Name })
                .ToListAsync(ct);

            var strongest = Math.Max(lovedGenres.Max(id => genreScores[id]), double.Epsilon);

            CandidateHit.Merge(hits, lovedGenres.SelectMany(genreId => rows
                .Where(row => row.GenreId == genreId)
                .Take(QuotaOf(
                    RecommendationTuning.Shelves.PerSourceLimit,
                    Math.Max(0, genreScores[genreId]) / strongest,
                    lovedGenres.Count))
                .Select(row => new CandidateHit(
                    row.Id,
                    CandidateSource.LovedGenres,
                    Content: 0.25 + 0.35 * Math.Max(0, genreScores[genreId]) / strongest,
                    ReasonKind: ReasonKinds.FromGenreYouLike,
                    ReasonSubject: row.GenreName,
                    ReasonSubjectId: row.GenreId))).ToList());
        }

        // Треки, которые соседствуют с затравками в плейлистах.
        var seedIds = seeds.Select(seed => seed.TrackId).ToList();
        if (seedIds.Count > 0)
        {
            var playlistIds = await db.PlaylistTracks.AsNoTracking()
                .Where(pt => seedIds.Contains(pt.TrackId))
                .Select(pt => pt.PlaylistId)
                .Distinct()
                .Take(PlaylistNeighbourCount)
                .ToListAsync(ct);

            if (playlistIds.Count > 0)
            {
                var rows = await db.PlaylistTracks.AsNoTracking()
                    .Where(pt => playlistIds.Contains(pt.PlaylistId) && !seedIds.Contains(pt.TrackId))
                    .GroupBy(pt => pt.TrackId)
                    .Select(group => new { TrackId = group.Key, Support = group.Count() })
                    .OrderByDescending(row => row.Support)
                    .Take(RecommendationTuning.Shelves.PerSourceLimit)
                    .ToListAsync(ct);

                CandidateHit.Merge(hits, rows.Select(row => new CandidateHit(
                    row.TrackId, CandidateSource.SharedPlaylists,
                    Collaborative: 0.35 + 0.15 * Math.Min(1, row.Support / 3.0),
                    ReasonKind: ReasonKinds.PopularWithSimilarTaste)).ToList());
            }
        }

        // Ближайшие к вектору вкуса.
        if (!snapshot.IsEmpty && taste.IsReady)
        {
            CandidateHit.Merge(hits, snapshot.TopK(taste.Query, RecommendationTuning.Shelves.PerSourceLimit)
                .Select(hit => new CandidateHit(
                    hit.TrackId,
                    CandidateSource.TasteVector,
                    Taste: Math.Max(0, hit.Score),
                    ReasonKind: ReasonKinds.MatchesYourTaste)).ToList());
        }

        // Новинки библиотеки и популярное.
        var fresh = db.Tracks.AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .Take(RecommendationTuning.Shelves.PerSourceLimit)
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
            .Take(RecommendationTuning.Shelves.PerSourceLimit)
            .Select(s => new
            {
                s.TrackId,
                Source = CandidateSource.Popular,
                s.Track!.ArtistId,
                ArtistName = s.Track.Artist!.Name,
                Popularity = s.PopularityScore,
            });

        CandidateHit.Merge(hits, (await fresh.Concat(popular).ToListAsync(ct)).Select(row =>
            row.Source == CandidateSource.Popular
                ? new CandidateHit(row.TrackId, CandidateSource.Popular,
                    Popularity: row.Popularity, ReasonKind: ReasonKinds.Trending)
                : artistScores.TryGetValue(row.ArtistId, out var score) && score > 0
                    ? new CandidateHit(row.TrackId, CandidateSource.NewReleases,
                        ReasonKind: ReasonKinds.NewFromArtistYouPlay,
                        ReasonSubject: row.ArtistName, ReasonSubjectId: row.ArtistId)
                    : new CandidateHit(row.TrackId, CandidateSource.NewReleases,
                        ReasonKind: ReasonKinds.FreshInLibrary)).ToList());

        // Ещё не слышанное.
        var unheard = await db.Tracks.AsNoTracking()
            .Where(t => !db.UserTrackAffinities.Any(a => a.UserId == userId && a.TrackId == t.Id))
            .ByPopularityThenNewest()
            .Take(RecommendationTuning.Shelves.PerSourceLimit)
            .Select(t => t.Id)
            .ToListAsync(ct);

        CandidateHit.Merge(hits, unheard.Select(id => new CandidateHit(
            id, CandidateSource.Unheard, ReasonKind: ReasonKinds.Discovery)).ToList());

        if (hits.Count > RecommendationTuning.Shelves.CandidateLimit)
        {
            hits = hits
                .OrderByDescending(pair => Math.Max(
                    Math.Max(pair.Value.Content, pair.Value.Collaborative),
                    Math.Max(Math.Max(pair.Value.Popularity, pair.Value.AudioSimilarity ?? 0), pair.Value.Taste ?? 0)))
                .ThenByDescending(pair => CandidateSources.Count(pair.Value.Families))
                .Take(RecommendationTuning.Shelves.CandidateLimit)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        if (hits.Count == 0)
            return (context, []);

        // Метаданные и признаки кандидатов.
        var trackIds = hits.Keys.ToList();

        var tracks = await db.Tracks.AsNoTracking()
            .Where(t => trackIds.Contains(t.Id))
            .Select(t => new
            {
                t.Id,
                t.ArtistId,
                t.AlbumId,
                t.GenreId,
                t.Year,
                t.CreatedAt,
                ArtistIds = t.TrackArtists.Select(ta => ta.ArtistId).ToList(),
                StatsPlayCount = t.Stats == null ? 0 : t.Stats.PlayCount,
                StatsSkipRate = t.Stats == null ? 0 : t.Stats.SkipRate,
            })
            .ToListAsync(ct);

        var topGenres = TopScoring(genreScores, 3).ToHashSet();

        // Близость к вкусу — перцентиль косинуса по всей библиотеке, чтобы шкала не зависела от модели.
        var tastePercentiles = new float[snapshot.Count];
        if (taste.Query.Length > 0 && !snapshot.IsEmpty)
        {
            var similarities = snapshot.SimilaritiesTo(taste.Query);
            var order = Enumerable.Range(0, similarities.Length).ToArray();
            Array.Sort(order, (a, b) => similarities[a].CompareTo(similarities[b]));

            for (var rank = 0; rank < order.Length; rank++)
                tastePercentiles[order[rank]] = order.Length == 1 ? 1f : rank / (float)(order.Length - 1);
        }

        var seedRows = seeds
            .Select(seed => (Row: snapshot.RowOf(seed.TrackId), seed.Weight))
            .Where(seed => seed.Row >= 0)
            .ToList();

        var candidates = new List<RecommendationCandidate>(tracks.Count);

        foreach (var track in tracks)
        {
            var hit = hits[track.Id];
            var credits = track.ArtistIds.Count > 0 ? track.ArtistIds : [track.ArtistId];
            var row = snapshot.RowOf(track.Id);

            double? seedSimilarity = null;
            if (row >= 0 && seedRows.Count > 0)
            {
                seedSimilarity = 0.0;
                foreach (var (seedRow, weight) in seedRows.Where(seed => seed.Row != row))
                    seedSimilarity = Math.Max(seedSimilarity.Value, weight * snapshot.Between(row, seedRow));
            }

            var ageDays = (now - track.CreatedAt).TotalDays;
            var freshnessWindow = RecommendationTuning.Shelves.FreshnessWindowDays;

            var candidate = new RecommendationCandidate
            {
                TrackId = track.Id,
                ArtistId = track.ArtistId,
                AlbumId = track.AlbumId,
                GenreId = track.GenreId,
                Year = track.Year,
                ArtistIds = credits,
                Source = hit.Source,
                Content = hit.Content,
                AudioSimilarity = seedSimilarity ?? hit.AudioSimilarity,
                TasteFit = row >= 0 && taste.Query.Length > 0 ? tastePercentiles[row] : hit.Taste,
                EmbeddingRow = row,
                Collaborative = hit.Collaborative,
                Popularity = hit.Popularity,
                Freshness = ageDays <= 0 ? 1 : ageDays >= freshnessWindow ? 0 : 1 - ageDays / freshnessWindow,
                Coverage = track.GenreId is not { } coverageGenre
                    ? 0.5
                    : genreShare!.TryGetValue(coverageGenre, out var share) ? 1 - share : 1,
                GlobalSkipRate = track.StatsPlayCount >= RecommendationTuning.Penalties.MinimumStatsSupport
                    ? track.StatsSkipRate
                    : null,
                EvidenceCount = Math.Max(1, CandidateSources.Count(hit.Families)),
                ReasonKind = hit.ReasonKind,
                ReasonSubject = hit.ReasonSubject,
                ReasonSubjectId = hit.ReasonSubjectId,
            };

            var knownArtist = credits.Any(id => artistScores.TryGetValue(id, out var score) && score > 0);
            var knownGenre = track.GenreId is { } genreId && topGenres.Contains(genreId);

            candidate.IsNovel = !history.ContainsKey(track.Id) && (!knownArtist || !knownGenre);

            candidates.Add(candidate);
        }

        logger.LogDebug("Generated {Count} candidates for user {UserId}", candidates.Count, userId);

        return (context, candidates);
    }

    private static int QuotaOf(int budget, double affinity, int shares)
    {
        var even = (double)budget / Math.Max(1, shares);

        return Math.Max(1, (int)Math.Ceiling(even * (0.25 + 0.75 * Math.Clamp(affinity, 0, 1))));
    }

    private static List<Guid> TopScoring(IReadOnlyDictionary<Guid, double> scores, int count) =>
    [
        .. scores
            .Where(pair => pair.Value > 0)
            .OrderByDescending(pair => pair.Value)
            .Take(count)
            .Select(pair => pair.Key)
    ];
}
