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
using MusicStreaming.Application.Recommendations.Sources;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

/// <summary>
/// Собирает пул кандидатов: грузит контекст пользователя, опрашивает независимые
/// <see cref="ICandidateSource"/> и материализует находки в <see cref="RecommendationCandidate"/>.
/// Сама логика «где брать треки» живёт в источниках, а не здесь.
/// </summary>
public class CandidateGenerator(
    IApplicationDbContext db,
    IEnumerable<ICandidateSource> sources,
    TrackNeighbourLookup neighbours,
    GlobalSource globalSource,
    IMemoryCache memoryCache,
    IOptions<RecommendationOptions> options,
    ILogger<CandidateGenerator> logger)
{
    private const int SeedTrackCount = 20;
    private const int RadioPoolFloor = 40;
    private static readonly TimeSpan GenreShareLifetime = TimeSpan.FromMinutes(5);
    private RecommendationOptions Options => options.Value;

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
                a.CompletedCount,
                a.SkipCount,
                a.ReplayCount,
                a.PlaylistAdds,
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
                    h.Score,
                    h.CompletedCount,
                    h.ReplayCount,
                    h.PlaylistAdds)),
            lastShown,
            now,
            profile.YearCenter,
            profile.YearSpread);

        var seeds = RecommendationSeedSelector.Select(ranking.History, now, SeedTrackCount);

        var suppressions = await db.RecommendationSuppressions.AsNoTracking()
            .Where(s => s.UserId == userId && (s.ExpiresAt == null || s.ExpiresAt > now))
            .Select(s => new { s.Target, s.TargetId })
            .ToListAsync(ct);

        return new UserRecommendationContext(userId, profile, ranking, seeds, await LoadGenreShareAsync(ct))
        {
            SuppressedTracks = suppressions
                .Where(s => s.Target == SuppressionTarget.Track)
                .Select(s => s.TargetId)
                .ToHashSet(),
            SuppressedArtists = suppressions
                .Where(s => s.Target == SuppressionTarget.Artist)
                .Select(s => s.TargetId)
                .ToHashSet(),
        };
    }

    public async Task<List<RecommendationCandidate>> GenerateAsync(
        UserRecommendationContext context, CancellationToken ct = default)
    {
        var hits = new Dictionary<Guid, CandidateHit>();

        // Порядок значим: объяснение достаётся источнику, назвавшему трек первым. Его задаёт
        // порядок регистрации в AddApplication, см. CandidateHits.Merge.
        foreach (var source in sources)
            CandidateHits.Merge(hits, await source.FetchAsync(context, ct));

        var candidates = await MaterialiseAsync(Cap(hits), context, ct);

        logger.LogDebug(
            "Generated {Count} candidates for user {UserId} ({Mode})",
            candidates.Count, context.UserId, context.IsColdStart ? "cold start" : "personalised");

        return candidates;
    }

    public async Task<List<RecommendationCandidate>> AroundAsync(
        UserRecommendationContext context, Guid seedTrackId, CancellationToken ct = default)
    {
        var hits = new Dictionary<Guid, CandidateHit>();
        var contextual = context.Seeds
            .Where(seed => seed.TrackId != seedTrackId)
            .Take(2)
            .ToList();
        var strongest = contextual.Count == 0 ? 1 : contextual.Max(seed => seed.Weight);
        var seeds = new List<RecommendationSeed> { new(seedTrackId, 1) };
        seeds.AddRange(contextual.Select(seed => seed with { Weight = 0.65 * seed.Weight / strongest }));

        CandidateHits.Merge(hits, await neighbours.NeighboursOfAsync(seeds, ct));

        if (hits.Count < RadioPoolFloor)
        {
            var related = await neighbours.SameArtistOrGenreAsync(seedTrackId, Options.PerSourceLimit, ct);

            CandidateHits.Merge(hits, related.Select(id => new CandidateHit(
                id, CandidateSource.SimilarToRecent, Content: 0.5, ReasonKind: ReasonKinds.SimilarTo)));
        }

        if (hits.Count < RadioPoolFloor)
            CandidateHits.Merge(hits, (await globalSource.FetchAsync(context, ct))
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
            id => new CandidateHit(
                id,
                CandidateSource.Rediscovery,
                Content: 0.6,
                ReasonKind: ReasonKinds.Rediscovery));

        return await MaterialiseAsync(hits, context, ct);
    }

    /// <summary>
    /// Восемь источников по <see cref="RecommendationOptions.PerSourceLimit"/> каждый дают заметно
    /// больше, чем нужно ранжированию, а материализация тянет метаданные на каждый трек. Срезаем
    /// самое слабое: сначала по силе сигнала, при равенстве — по числу подтвердивших семейств.
    /// </summary>
    private Dictionary<Guid, CandidateHit> Cap(Dictionary<Guid, CandidateHit> hits)
    {
        if (hits.Count <= Options.CandidateLimit)
            return hits;

        return hits
            .OrderByDescending(pair => Strength(pair.Value))
            .ThenByDescending(pair => CandidateSources.Count(pair.Value.Families))
            .Take(Options.CandidateLimit)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static double Strength(CandidateHit hit) => Math.Max(
        Math.Max(hit.Content, hit.Collaborative),
        Math.Max(hit.Popularity, hit.AudioSimilarity ?? 0));

    private async Task<List<RecommendationCandidate>> MaterialiseAsync(
        Dictionary<Guid, CandidateHit> hits, UserRecommendationContext context, CancellationToken ct)
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
                StatsPlayCount = t.Stats == null ? 0 : t.Stats.PlayCount,
                StatsSkipRate = t.Stats == null ? 0 : t.Stats.SkipRate,
                HasAudio = t.AudioFeatures != null && t.AudioFeatures.Succeeded,
                Tempo = t.AudioFeatures == null ? null : t.AudioFeatures.TempoBpm,
                Energy = t.AudioFeatures == null ? 0 : t.AudioFeatures.Energy,
                Brightness = t.AudioFeatures == null ? 0 : t.AudioFeatures.Brightness,
            })
            .ToListAsync(ct);

        var topGenres = SourceQuota.TopScoring(context.Ranking.GenreScores, 3).ToHashSet();
        var candidates = new List<RecommendationCandidate>(rows.Count);

        foreach (var row in rows)
        {
            var hit = hits[row.Id];
            var credits = row.ArtistIds.Count > 0 ? row.ArtistIds : [row.ArtistId];

            // Явное «не интересно» — это запрет, а не ещё один штраф в скоринге.
            if (context.IsSuppressed(row.Id, credits))
                continue;

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
                AudioSimilarity = hit.AudioSimilarity,
                Collaborative = hit.Collaborative,
                Popularity = hit.Popularity,
                Freshness = AffinityMath.Freshness(row.CreatedAt, now, Options.FreshnessWindowDays),
                Coverage = CoverageFor(row.GenreId, context),
                AudioProfile = row.HasAudio
                    ? new TrackAudioProfile(row.Tempo, row.Energy, row.Brightness)
                    : null,
                GlobalSkipRate = row.StatsPlayCount >= Options.MinimumStatsSupport
                    ? row.StatsSkipRate
                    : null,
                EvidenceCount = Math.Max(1, CandidateSources.Count(hit.Families)),
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
}
