// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Application.Recommendations.Scoring;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

public record UserRecommendationContext(
    Guid UserId,
    UserTasteProfile Profile,
    RankingContext Ranking,
    IReadOnlyList<Guid> SeedTrackIds,
    IReadOnlyDictionary<Guid, double> GenreShare)
{
    public bool IsColdStart => Profile.PositiveSignalCount == 0;
}

public class CandidateGenerator(
    IApplicationDbContext db,
    IMemoryCache memoryCache,
    IOptions<RecommendationOptions> options,
    ILogger<CandidateGenerator> logger)
{
    private const int SeedTrackCount = 20;
    private const int TopArtistCount = 8;
    private const int TopGenreCount = 4;
    private const int NeighbourCount = 20;
    private const int MinimumNeighbourOverlap = 3;
    private const int RadioPoolFloor = 40;
    private static readonly TimeSpan GenreShareLifetime = TimeSpan.FromMinutes(5);
    private RecommendationOptions Options => options.Value;

    private record Hit(
        Guid TrackId,
        CandidateSource Source,
        double Content = 0,
        double Collaborative = 0,
        double Popularity = 0,
        string ReasonKind = ReasonKinds.Discovery,
        string? ReasonSubject = null,
        Guid? ReasonSubjectId = null);

    public async Task<UserRecommendationContext> LoadContextAsync(
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

        var history = await db.UserTrackAffinities.AsNoTracking()
            .Where(a => a.UserId == userId)
            .Select(a => new
            {
                a.TrackId,
                a.LastPlayedAt,
                a.PlayCount,
                a.SkipCount,
                a.CompletionSum,
                a.CompletionSamples,
                a.Score,
            })
            .ToListAsync(ct);

        var cooldown = now.AddDays(-Options.ImpressionCooldownDays);
        var lastShown = await db.RecommendationImpressions.AsNoTracking()
            .Where(i => i.UserId == userId && i.ShownAt >= cooldown && i.ClickedAt == null)
            .GroupBy(i => i.TrackId)
            .Select(g => new { TrackId = g.Key, ShownAt = g.Max(i => i.ShownAt) })
            .ToDictionaryAsync(x => x.TrackId, x => x.ShownAt, ct);

        var seeds = history
            .Where(h => h.Score > 0)
            .OrderByDescending(h => h.LastPlayedAt)
            .Take(SeedTrackCount)
            .Select(h => h.TrackId)
            .ToList();

        var ranking = new RankingContext(
            artistScores,
            genreScores,
            history.ToDictionary(
                h => h.TrackId,
                h => new TrackHistory(
                    h.LastPlayedAt,
                    h.PlayCount,
                    h.SkipCount,
                    h.CompletionSamples == 0 ? 0 : h.CompletionSum / h.CompletionSamples,
                    h.Score)),
            lastShown,
            now);

        return new UserRecommendationContext(userId, profile, ranking, seeds, await LoadGenreShareAsync(ct));
    }

    public async Task<List<RecommendationCandidate>> GenerateAsync(
        UserRecommendationContext context, CancellationToken ct = default)
    {
        var hits = new Dictionary<Guid, Hit>();

        Merge(hits, await ContinueListeningAsync(context, ct));
        Merge(hits, await SimilarToRecentAsync(context, ct));
        Merge(hits, await FromLovedArtistsAsync(context, ct));
        Merge(hits, await FromSimilarListenersAsync(context, ct));
        Merge(hits, await FromLovedGenresAsync(context, ct));
        Merge(hits, await FromSharedPlaylistsAsync(context, ct));
        Merge(hits, await GlobalSourcesAsync(context, ct));
        Merge(hits, await UnheardAsync(context, ct));

        var candidates = await MaterialiseAsync(hits, context, ct);

        logger.LogDebug(
            "Generated {Count} candidates for user {UserId} ({Mode})",
            candidates.Count, context.UserId, context.IsColdStart ? "cold start" : "personalised");

        return candidates;
    }

    public async Task<List<RecommendationCandidate>> AroundAsync(
        UserRecommendationContext context, Guid seedTrackId, CancellationToken ct = default)
    {
        var hits = new Dictionary<Guid, Hit>();
        Merge(hits, await NeighboursOfAsync(seedTrackId, ct));

        if (hits.Count < RadioPoolFloor)
        {
            var related = await SameArtistOrGenreAsync(seedTrackId, Options.PerSourceLimit, ct);

            Merge(hits, related.Select(id => new Hit(
                id, CandidateSource.SimilarToRecent, Content: 0.5, ReasonKind: ReasonKinds.SimilarTo)));
        }

        if (hits.Count < RadioPoolFloor)
            Merge(hits, (await GlobalSourcesAsync(context, ct))
                .Where(hit => hit.Source == CandidateSource.Popular));

        hits.Remove(seedTrackId);

        return await MaterialiseAsync(hits, context, ct);
    }

    public async Task<List<RecommendationCandidate>> RediscoverAsync(
        UserRecommendationContext context, CancellationToken ct = default)
    {
        var trackIds = await db.UserTrackAffinities.AsNoTracking()
            .Where(a => a.UserId == context.UserId && a.Score > 0)
            .OrderBy(a => a.LastPlayedAt)
            .Take(Options.CandidateLimit)
            .Select(a => a.TrackId)
            .ToListAsync(ct);

        var hits = trackIds.ToDictionary(
            id => id,
            id => new Hit(
                id,
                CandidateSource.Rediscovery,
                Content: 0.6,
                ReasonKind: ReasonKinds.Rediscovery));

        return await MaterialiseAsync(hits, context, ct);
    }

    private async Task<List<Hit>> NeighboursOfAsync(Guid seedTrackId, CancellationToken ct)
    {
        var rows = await db.TrackSimilarities.AsNoTracking()
            .Where(s => s.TrackId == seedTrackId)
            .OrderByDescending(s => s.Score)
            .Take(Options.PerSourceLimit)
            .Select(s => new
            {
                s.SimilarTrackId,
                s.ContentScore,
                s.CollabScore,
                SeedTitle = s.Track!.Title,
            })
            .ToListAsync(ct);

        return [.. rows.Select(row => new Hit(
            row.SimilarTrackId,
            CandidateSource.SimilarToRecent,
            row.ContentScore,
            row.CollabScore,
            ReasonKind: ReasonKinds.SimilarTo,
            ReasonSubject: row.SeedTitle,
            ReasonSubjectId: seedTrackId))];
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

    private static void Merge(Dictionary<Guid, Hit> pool, IEnumerable<Hit> produced)
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
                Collaborative = Math.Max(existing.Collaborative, hit.Collaborative),
                Popularity = Math.Max(existing.Popularity, hit.Popularity),
            };
        }
    }

    private async Task<List<RecommendationCandidate>> MaterialiseAsync(
        Dictionary<Guid, Hit> hits, UserRecommendationContext context, CancellationToken ct)
    {
        if (hits.Count == 0)
            return [];

        var trackIds = hits.Keys.ToList();
        var now = context.Ranking.Now;

        var rows = await db.Tracks.AsNoTracking()
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
            })
            .ToListAsync(ct);

        var topGenres = TopScoring(context.Ranking.GenreScores, 3).ToHashSet();
        var candidates = new List<RecommendationCandidate>(rows.Count);

        foreach (var row in rows)
        {
            var hit = hits[row.Id];
            var credits = row.ArtistIds.Count > 0 ? row.ArtistIds : [row.ArtistId];

            var candidate = new RecommendationCandidate
            {
                TrackId = row.Id,
                ArtistId = row.ArtistId,
                AlbumId = row.AlbumId,
                GenreId = row.GenreId,
                Year = row.Year,
                ArtistIds = credits,
                Source = hit.Source,
                Content = hit.Content,
                Collaborative = hit.Collaborative,
                Popularity = hit.Popularity,
                Freshness = AffinityMath.Freshness(row.CreatedAt, now, Options.FreshnessWindowDays),
                Coverage = CoverageFor(row.GenreId, context),
                ReasonKind = hit.ReasonKind,
                ReasonSubject = hit.ReasonSubject,
                ReasonSubjectId = hit.ReasonSubjectId,
            };

            var knownArtist = credits.Any(id =>
                context.Ranking.ArtistScores.TryGetValue(id, out var score) && score > 0);
            var knownGenre = row.GenreId is { } genreId && topGenres.Contains(genreId);

            candidate.IsNovel = !context.Ranking.History.ContainsKey(row.Id) && (!knownArtist || !knownGenre);

            candidates.Add(candidate);
        }

        return candidates;
    }

    private async Task<List<Hit>> ContinueListeningAsync(UserRecommendationContext context, CancellationToken ct)
    {
        var since = context.Ranking.Now.AddDays(-Options.RecentlyPlayedDays);

        var trackIds = await db.UserTrackAffinities.AsNoTracking()
            .Where(a => a.UserId == context.UserId
                        && a.LastPlayedAt >= since
                        && a.CompletionSamples > 0
                        && a.CompletedCount == 0
                        && a.SkipCount == 0)
            .OrderByDescending(a => a.LastPlayedAt)
            .Take(Options.ShelfSize * 2)
            .Select(a => a.TrackId)
            .ToListAsync(ct);

        return trackIds
            .Select(id => new Hit(id, CandidateSource.ContinueListening,
                Content: 1, ReasonKind: ReasonKinds.ContinueListening))
            .ToList();
    }

    private async Task<List<Hit>> SimilarToRecentAsync(UserRecommendationContext context, CancellationToken ct)
    {
        var seeds = context.SeedTrackIds;
        if (seeds.Count == 0)
            return [];

        var rows = await db.TrackSimilarities.AsNoTracking()
            .Where(s => seeds.Contains(s.TrackId))
            .OrderByDescending(s => s.Score)
            .Take(Options.PerSourceLimit)
            .Select(s => new
            {
                s.SimilarTrackId,
                s.ContentScore,
                s.CollabScore,
                SeedTitle = s.Track!.Title,
                SeedArtist = s.Track!.Artist!.Name,
                SeedArtistId = s.Track!.ArtistId,
            })
            .ToListAsync(ct);

        return rows.Select(row =>
            row.CollabScore > row.ContentScore
                ? new Hit(row.SimilarTrackId, CandidateSource.SimilarToRecent,
                    row.ContentScore, row.CollabScore,
                    ReasonKind: ReasonKinds.SimilarTo, ReasonSubject: row.SeedTitle)
                : new Hit(row.SimilarTrackId, CandidateSource.SimilarToRecent,
                    row.ContentScore, row.CollabScore,
                    ReasonKind: ReasonKinds.BecauseYouListened,
                    ReasonSubject: row.SeedArtist, ReasonSubjectId: row.SeedArtistId))
            .ToList();
    }

    private async Task<List<Hit>> FromLovedArtistsAsync(UserRecommendationContext context, CancellationToken ct)
    {
        var artists = TopScoring(context.Ranking.ArtistScores, TopArtistCount);
        if (artists.Count == 0)
            return [];

        var rows = await db.Tracks.AsNoTracking()
            .Where(t => t.TrackArtists.Any(ta => artists.Contains(ta.ArtistId)))
            .OrderByDescending(t => t.CreatedAt)
            .Take(Options.PerSourceLimit)
            .Select(t => new { t.Id, t.ArtistId, ArtistName = t.Artist!.Name })
            .ToListAsync(ct);

        return rows.Select(row => new Hit(
            row.Id, CandidateSource.LovedArtists, Content: 0.7,
            ReasonKind: ReasonKinds.BecauseYouListened,
            ReasonSubject: row.ArtistName, ReasonSubjectId: row.ArtistId)).ToList();
    }

    private async Task<List<Hit>> FromSimilarListenersAsync(
        UserRecommendationContext context, CancellationToken ct)
    {
        var eligibleUsers = await db.UserTasteProfiles.AsNoTracking()
            .CountAsync(p => p.PositiveSignalCount >= Options.UserCfMinInteractions, ct);

        if (eligibleUsers < Options.UserCfMinUsers)
            return [];

        var liked = await db.UserTrackAffinities.AsNoTracking()
            .Where(a => a.UserId == context.UserId && a.Score > 0)
            .Select(a => a.TrackId)
            .ToListAsync(ct);

        if (liked.Count == 0)
            return [];

        var neighbours = await db.UserTrackAffinities.AsNoTracking()
            .Where(a => a.UserId != context.UserId && a.Score > 0 && liked.Contains(a.TrackId))
            .GroupBy(a => a.UserId)
            .Select(g => new { UserId = g.Key, Overlap = g.Count() })
            .Where(x => x.Overlap >= MinimumNeighbourOverlap)
            .OrderByDescending(x => x.Overlap)
            .Take(NeighbourCount)
            .Select(x => x.UserId)
            .ToListAsync(ct);

        if (neighbours.Count == 0)
            return [];

        var rows = await db.UserTrackAffinities.AsNoTracking()
            .Where(a => neighbours.Contains(a.UserId) && a.Score > 0.2 && !liked.Contains(a.TrackId))
            .OrderByDescending(a => a.Score)
            .Take(Options.PerSourceLimit)
            .Select(a => new { a.TrackId, a.Score })
            .ToListAsync(ct);

        return rows.Select(row => new Hit(
            row.TrackId, CandidateSource.SimilarListeners,
            Collaborative: Math.Min(1, row.Score),
            ReasonKind: ReasonKinds.PopularWithSimilarTaste)).ToList();
    }

    private async Task<List<Hit>> FromLovedGenresAsync(UserRecommendationContext context, CancellationToken ct)
    {
        var genres = TopScoring(context.Ranking.GenreScores, TopGenreCount);
        if (genres.Count == 0)
            return [];

        var rows = await db.Tracks.AsNoTracking()
            .Where(t => t.GenreId != null && genres.Contains(t.GenreId.Value))
            .OrderByDescending(t => t.CreatedAt)
            .Take(Options.PerSourceLimit)
            .Select(t => new { t.Id, t.GenreId, GenreName = t.Genre!.Name })
            .ToListAsync(ct);

        return rows.Select(row => new Hit(
            row.Id, CandidateSource.LovedGenres, Content: 0.4,
            ReasonKind: ReasonKinds.FromGenreYouLike,
            ReasonSubject: row.GenreName, ReasonSubjectId: row.GenreId)).ToList();
    }

    private async Task<List<Hit>> FromSharedPlaylistsAsync(UserRecommendationContext context, CancellationToken ct)
    {
        var seeds = context.SeedTrackIds;
        if (seeds.Count == 0)
            return [];

        var playlistIds = await db.PlaylistTracks.AsNoTracking()
            .Where(pt => seeds.Contains(pt.TrackId))
            .Select(pt => pt.PlaylistId)
            .Distinct()
            .Take(NeighbourCount)
            .ToListAsync(ct);

        if (playlistIds.Count == 0)
            return [];

        var trackIds = await db.PlaylistTracks.AsNoTracking()
            .Where(pt => playlistIds.Contains(pt.PlaylistId) && !seeds.Contains(pt.TrackId))
            .Select(pt => pt.TrackId)
            .Distinct()
            .Take(Options.PerSourceLimit)
            .ToListAsync(ct);

        return trackIds.Select(id => new Hit(
            id, CandidateSource.SharedPlaylists, Collaborative: 0.5,
            ReasonKind: ReasonKinds.PopularWithSimilarTaste)).ToList();
    }

    private async Task<List<Hit>> GlobalSourcesAsync(
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
                ArtistId = s.TrackId,
                ArtistName = string.Empty,
                Popularity = s.PopularityScore,
            });

        var rows = await fresh.Concat(popular).ToListAsync(ct);

        return rows.Select(row =>
        {
            if (row.Source == CandidateSource.Popular)
            {
                return new Hit(
                    row.TrackId,
                    CandidateSource.Popular,
                    Popularity: row.Popularity,
                    ReasonKind: ReasonKinds.Trending);
            }

            var artistId = row.ArtistId;
            var known = context.Ranking.ArtistScores.TryGetValue(artistId, out var score) && score > 0;

            return known
                ? new Hit(row.TrackId, CandidateSource.NewReleases,
                    ReasonKind: ReasonKinds.NewFromArtistYouPlay,
                    ReasonSubject: row.ArtistName, ReasonSubjectId: artistId)
                : new Hit(row.TrackId, CandidateSource.NewReleases, ReasonKind: ReasonKinds.FreshInLibrary);
        }).ToList();
    }

    private async Task<List<Hit>> UnheardAsync(UserRecommendationContext context, CancellationToken ct)
    {
        var userId = context.UserId;

        var trackIds = await db.Tracks.AsNoTracking()
            .Where(t => !db.UserTrackAffinities.Any(a => a.UserId == userId && a.TrackId == t.Id))
            .OrderByDescending(t => t.CreatedAt)
            .Take(Options.PerSourceLimit)
            .Select(t => t.Id)
            .ToListAsync(ct);

        return trackIds
            .Select(id => new Hit(id, CandidateSource.Unheard, ReasonKind: ReasonKinds.Discovery))
            .ToList();
    }

    private static double CoverageFor(Guid? genreId, UserRecommendationContext context)
    {
        if (genreId is not { } id)
            return 0.5;

        return context.GenreShare.TryGetValue(id, out var share) ? 1 - share : 1;
    }

    private async Task<IReadOnlyDictionary<Guid, double>> LoadGenreShareAsync(CancellationToken ct)
    {
        if (memoryCache.TryGetValue(RecommendationCacheKeys.GenreShare, out IReadOnlyDictionary<Guid, double>? cached)
            && cached is not null)
        {
            return cached;
        }

        var counts = await db.Tracks.AsNoTracking()
            .Where(t => t.GenreId != null)
            .GroupBy(t => t.GenreId!.Value)
            .Select(g => new { GenreId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var total = counts.Sum(c => c.Count);
        IReadOnlyDictionary<Guid, double> share = total == 0
            ? new Dictionary<Guid, double>()
            : counts.ToDictionary(c => c.GenreId, c => (double)c.Count / total);

        memoryCache.Set(RecommendationCacheKeys.GenreShare, share, GenreShareLifetime);
        return share;
    }

    private static List<Guid> TopScoring(IReadOnlyDictionary<Guid, double> scores, int count) =>
        scores
            .Where(pair => pair.Value > 0)
            .OrderByDescending(pair => pair.Value)
            .Take(count)
            .Select(pair => pair.Key)
            .ToList();
}
