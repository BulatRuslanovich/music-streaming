// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Application.Recommendations.Embeddings;
using MusicStreaming.Application.Recommendations.Scoring;
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
    IEmbeddingIndex embeddingIndex,
    TasteVectorReader tasteVectors,
    IMemoryCache memoryCache,
    ILogger<CandidateGenerator> logger)
{
    private const int SeedTrackCount = 20;
    private static readonly TimeSpan GenreShareLifetime = TimeSpan.FromMinutes(5);

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
            now,
            profile.YearCenter,
            profile.YearSpread);

        var seeds = RecommendationSeedSelector.Select(ranking.History, now, SeedTrackCount);

        var genreShare = await memoryCache.GetOrCreateAsync(RecommendationCacheKeys.GenreShare, async entry =>
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

        return new UserRecommendationContext(userId, profile, ranking, seeds, genreShare!);
    }

    public async Task<List<RecommendationCandidate>> GenerateAsync(
        UserRecommendationContext context, CancellationToken ct = default)
    {
        var hits = new Dictionary<Guid, CandidateHit>();

        // Порядок значим: объяснение достаётся источнику, назвавшему трек первым. Его задаёт
        // порядок регистрации в AddApplication, см. CandidateHits.Merge.
        foreach (var source in sources)
            CandidateHits.Merge(hits, await source.FetchAsync(context, ct));

        // Источники по RecommendationTuning.Shelves.PerSourceLimit каждый дают заметно больше,
        // чем нужно ранжированию, а материализация тянет метаданные на каждый трек. Срезаем самое
        // слабое: сначала по силе сигнала, при равенстве — по числу подтвердивших семейств. Taste
        // входит в силу наравне с остальными: иначе трек, найденный только по звучанию, срезался
        // бы отсечкой раньше всех — ровно тот случай, ради которого эмбеддинги и добавлялись.
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

        var candidates = await MaterialiseAsync(hits, context, ct);

        logger.LogDebug(
            "Generated {Count} candidates for user {UserId} ({Mode})",
            candidates.Count, context.UserId, context.IsColdStart ? "cold start" : "personalised");

        return candidates;
    }

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
            })
            .ToListAsync(ct);

        var topGenres = SourceQuota.TopScoring(context.Ranking.GenreScores, 3).ToHashSet();
        var candidates = new List<RecommendationCandidate>(rows.Count);

        // Сигналы звучания берутся из матрицы в памяти, а не из track_similarity: одним проходом
        // по индексу считаются и близость к сидам, и близость к вектору вкуса. Вектор вкуса
        // берётся с догоном непрошедших свёртку событий, чтобы полки и очередь радио одинаково
        // понимали, что такое «ваш вкус».
        var snapshot = embeddingIndex.Snapshot();
        var sonic = snapshot.IsEmpty
            ? SonicSignals.None
            : SonicSignals.Build(snapshot, (await tasteVectors.CurrentAsync(context.UserId, snapshot, ct)).Query, context.Seeds);

        foreach (var row in rows)
        {
            var hit = hits[row.Id];
            var credits = row.ArtistIds.Count > 0 ? row.ArtistIds : [row.ArtistId];
            var signals = sonic.For(row.Id);

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
                AudioSimilarity = signals.SeedSimilarity ?? hit.AudioSimilarity,
                TasteFit = signals.TasteFit ?? hit.Taste,
                EmbeddingRow = signals.Row,
                Collaborative = hit.Collaborative,
                Popularity = hit.Popularity,
                Freshness = AffinityMath.Freshness(row.CreatedAt, now, RecommendationTuning.Shelves.FreshnessWindowDays),
                Coverage = row.GenreId is not { } coverageGenre
                    ? 0.5
                    : context.GenreShare.TryGetValue(coverageGenre, out var share) ? 1 - share : 1,
                GlobalSkipRate = row.StatsPlayCount >= RecommendationTuning.Penalties.MinimumStatsSupport
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
}
