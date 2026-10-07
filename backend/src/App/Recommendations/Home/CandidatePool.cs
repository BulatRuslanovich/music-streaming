// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Infrastructure.Persistence;
using System.Numerics;
using App.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using App.Recommendations.Embeddings;

namespace App.Recommendations.Home;

public class CandidatePool(
    ApplicationDbContext db,
    EmbeddingIndex embeddingIndex,
    IMemoryCache memoryCache,
    ILogger<CandidatePool> logger)
{
    private const int SonicSeedCount = 4;
    private const int TopArtistCount = 8;
    private const int TopGenreCount = 4;
    private const int PlaylistNeighbourCount = 20;
    private const string GenreShareCacheKey = "recommendations:genre-share";
    private static readonly TimeSpan GenreShareLifetime = TimeSpan.FromMinutes(5);

    public async Task<(UserRecommendationContext Context, List<RecommendationCandidate> Candidates)> LoadAsync(
        Guid userId, DateTimeOffset now, CancellationToken ct = default)
    {
        var snapshot = embeddingIndex.Snapshot();
        var context = await UserRecommendationContext.LoadAsync(db, snapshot, userId, now, ct);

        var artistScores = context.Ranking.ArtistScores;
        var genreScores = context.Ranking.GenreScores;
        var history = context.Ranking.History;
        var seeds = context.Seeds;
        var taste = context.Taste;

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

        // Источники только собирают кандидатов и объясняют, почему трек попал на полку.
        // Порядок важен: причину задаёт первый источник, предложивший трек.
        var reasons = new Dictionary<Guid, (string Kind, string? Subject, Guid? SubjectId)>();

        // Сколько независимых каналов нашли трек: согласие каналов — отдельный сигнал качества.
        var evidence = new Dictionary<Guid, Evidence>();

        void Offer(Evidence channel, Guid trackId, string kind, string? subject = null, Guid? subjectId = null)
        {
            reasons.TryAdd(trackId, (kind, subject, subjectId));
            evidence[trackId] = evidence.GetValueOrDefault(trackId) | channel;
        }

        // Похожие по звучанию на самые сильные затравки.
        // Сначала самая сильная затравка каждого центра вкуса, чтобы слабый вкус тоже дал соседей.
        var embeddedSeeds = snapshot.IsEmpty ? [] : seeds.Where(seed => snapshot.RowOf(seed.TrackId) >= 0).ToList();
        var sonicSeeds = embeddedSeeds
            .GroupBy(seed => taste.NearestMode(snapshot.Vector(snapshot.RowOf(seed.TrackId))))
            .Select(group => group.First())
            .Concat(embeddedSeeds)
            .DistinctBy(seed => seed.TrackId)
            .Take(SonicSeedCount)
            .ToList();

        if (sonicSeeds.Count > 0)
        {
            var perSeed = Math.Max(1, RecommendationTuning.Shelves.PerSourceLimit / sonicSeeds.Count);
            var sonicSeedIds = sonicSeeds.Select(seed => seed.TrackId).ToList();
            var titles = await db.Tracks.AsNoTracking()
                .Where(track => sonicSeedIds.Contains(track.Id))
                .ToDictionaryAsync(track => track.Id, track => track.Title, ct);

            foreach (var seed in sonicSeeds)
            {
                var family = snapshot.CloneIds(seed.TrackId).ToHashSet();

                foreach (var neighbour in snapshot.TopK(snapshot.Vector(snapshot.RowOf(seed.TrackId)), perSeed, family))
                    Offer(Evidence.Sound, neighbour.TrackId, ReasonKinds.SoundsLike, titles.GetValueOrDefault(seed.TrackId), seed.TrackId);
            }
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

            foreach (var artistId in lovedArtists)
            {
                var picks = rows
                    .Where(row => row.Matches.Any(match => match.ArtistId == artistId))
                    .OrderByDescending(row => row.Popularity)
                    .ThenByDescending(row => row.CreatedAt)
                    .Take(QuotaOf(RecommendationTuning.Shelves.PerSourceLimit, Math.Max(0, artistScores[artistId]) / strongest, lovedArtists.Count));

                foreach (var row in picks)
                {
                    Offer(Evidence.Taste, row.Id, ReasonKinds.BecauseYouListened,
                        row.Matches.First(item => item.ArtistId == artistId).ArtistName, artistId);
                }
            }
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

            foreach (var genreId in lovedGenres)
            {
                var picks = rows
                    .Where(row => row.GenreId == genreId)
                    .Take(QuotaOf(RecommendationTuning.Shelves.PerSourceLimit, Math.Max(0, genreScores[genreId]) / strongest, lovedGenres.Count));

                foreach (var row in picks)
                    Offer(Evidence.Taste, row.Id, ReasonKinds.FromGenreYouLike, row.GenreName, row.GenreId);
            }
        }

        // Треки, которые соседствуют с затравками в плейлистах; число общих плейлистов — признак трека.
        var seedIds = seeds.Select(seed => seed.TrackId).ToList();
        var playlistSupport = new Dictionary<Guid, int>();

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
                playlistSupport = await db.PlaylistTracks.AsNoTracking()
                    .Where(pt => playlistIds.Contains(pt.PlaylistId) && !seedIds.Contains(pt.TrackId))
                    .GroupBy(pt => pt.TrackId)
                    .Select(group => new { TrackId = group.Key, Support = group.Count() })
                    .ToDictionaryAsync(row => row.TrackId, row => row.Support, ct);

                foreach (var (trackId, _) in playlistSupport.OrderByDescending(pair => pair.Value).Take(RecommendationTuning.Shelves.PerSourceLimit))
                    Offer(Evidence.Playlists, trackId, ReasonKinds.PopularWithSimilarTaste);
            }
        }

        // Ближайшие к каждому центру вкуса; квота центра растёт с его долей, но не ниже четверти поровну.
        if (!snapshot.IsEmpty && !taste.IsEmpty)
        {
            foreach (var mode in taste.Modes)
            {
                var quota = QuotaOf(RecommendationTuning.Shelves.PerSourceLimit, mode.Share * taste.Modes.Count, taste.Modes.Count);

                foreach (var hit in snapshot.TopK(mode.Centre, quota))
                    Offer(Evidence.Sound, hit.TrackId, ReasonKinds.MatchesYourTaste);
            }
        }

        // Популярное и новинки библиотеки.
        var popular = await db.TrackStats.AsNoTracking()
            .Where(s => s.PopularityScore > 0)
            .OrderByDescending(s => s.PopularityScore)
            .Take(RecommendationTuning.Shelves.PerSourceLimit)
            .Select(s => s.TrackId)
            .ToListAsync(ct);

        var fresh = await db.Tracks.AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .Take(RecommendationTuning.Shelves.PerSourceLimit)
            .Select(t => new { t.Id, t.ArtistId, ArtistName = t.Artist!.Name })
            .ToListAsync(ct);

        foreach (var row in fresh)
        {
            if (artistScores.TryGetValue(row.ArtistId, out var score) && score > 0)
                Offer(Evidence.Library, row.Id, ReasonKinds.NewFromArtistYouPlay, row.ArtistName, row.ArtistId);
            else
                Offer(Evidence.Library, row.Id, ReasonKinds.FreshInLibrary);
        }

        foreach (var trackId in popular)
            Offer(Evidence.Library, trackId, ReasonKinds.Trending);

        // Ещё не слышанное.
        var unheard = await db.Tracks.AsNoTracking()
            .Where(t => !db.UserTrackAffinities.Any(a => a.UserId == userId && a.TrackId == t.Id))
            .ByPopularityThenNewest()
            .Take(RecommendationTuning.Shelves.PerSourceLimit)
            .Select(t => t.Id)
            .ToListAsync(ct);

        foreach (var trackId in unheard)
            Offer(Evidence.Library, trackId, ReasonKinds.Discovery);

        if (reasons.Count == 0)
            return (context, []);

        // Признаки считаются для каждого трека одинаково, независимо от того, каким источником он пришёл.
        var trackIds = reasons.Keys.ToList();

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
                Popularity = t.Stats == null ? 0 : t.Stats.PopularityScore,
            })
            .ToListAsync(ct);

        var topGenres = TopScoring(genreScores, 3).ToHashSet();

        var strongestArtist = lovedArtists.Count == 0 ? double.Epsilon : Math.Max(artistScores[lovedArtists[0]], double.Epsilon);
        var strongestGenre = lovedGenres.Count == 0 ? double.Epsilon : Math.Max(genreScores[lovedGenres[0]], double.Epsilon);

        // Близость к вкусу — перцентиль косинуса по всей библиотеке, чтобы шкала не зависела от модели.
        var tastePercentiles = taste.IsEmpty
            ? new float[snapshot.Count]
            : VectorMath.PercentileRanks(taste.SimilaritiesIn(snapshot));

        var seedRows = seeds
            .Select(seed => (Row: snapshot.RowOf(seed.TrackId), seed.Weight))
            .Where(seed => seed.Row >= 0)
            .ToList();

        var candidates = new List<RecommendationCandidate>(tracks.Count);

        foreach (var track in tracks)
        {
            var reason = reasons[track.Id];
            var credits = track.ArtistIds.Count > 0 ? track.ArtistIds : [track.ArtistId];
            var row = snapshot.RowOf(track.Id);

            // Сходство со звучанием затравок: лучшая из взвешенных косинусных близостей.
            double? seedSimilarity = null;
            if (row >= 0 && seedRows.Count > 0)
            {
                seedSimilarity = 0.0;
                foreach (var (seedRow, weight) in seedRows.Where(seed => seed.Row != row))
                    seedSimilarity = Math.Max(seedSimilarity.Value, weight * snapshot.Between(row, seedRow));
            }

            var lovedArtist = credits.Where(lovedArtists.Contains).Select(id => artistScores[id]).DefaultIfEmpty().Max();
            var lovedGenre = track.GenreId is { } lovedGenreId && lovedGenres.Contains(lovedGenreId) ? genreScores[lovedGenreId] : 0;

            var content = Math.Max(
                lovedArtist > 0 ? 0.45 + 0.35 * lovedArtist / strongestArtist : 0,
                lovedGenre > 0 ? 0.25 + 0.35 * lovedGenre / strongestGenre : 0);

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
                Content = content,
                EvidenceCount = BitOperations.PopCount((uint)evidence[track.Id]),
                AudioSimilarity = seedSimilarity,
                TasteFit = row >= 0 && !taste.IsEmpty ? tastePercentiles[row] : null,
                EmbeddingRow = row,
                Collaborative = playlistSupport.TryGetValue(track.Id, out var support)
                    ? 0.35 + 0.15 * Math.Min(1, support / 3.0)
                    : 0,
                Popularity = PopularityAmongOthers(track.Popularity, history.GetValueOrDefault(track.Id), now),
                Freshness = ageDays <= 0 ? 1 : ageDays >= freshnessWindow ? 0 : 1 - ageDays / freshnessWindow,
                Coverage = track.GenreId is not { } coverageGenre
                    ? 0.5
                    : genreShare!.TryGetValue(coverageGenre, out var share) ? 1 - share : 1,
                ReasonKind = reason.Kind,
                ReasonSubject = reason.Subject,
                ReasonSubjectId = reason.SubjectId,
            };

            var knownArtist = credits.Any(id => artistScores.TryGetValue(id, out var score) && score > 0);
            var knownGenre = track.GenreId is { } genreId && topGenres.Contains(genreId);

            candidate.IsFamiliar = history.TryGetValue(track.Id, out var heard) && heard.PlayCount > 0;
            candidate.IsNovel = !history.ContainsKey(track.Id) && (!knownArtist || !knownGenre);

            candidates.Add(candidate);
        }

        logger.LogDebug("Generated {Count} candidates for user {UserId}", candidates.Count, userId);

        return (context, candidates);
    }

    // PopularityScore = x / (x + 10), где x = 2 * прослушивания за 30 дней + все прослушивания — по всем
    // пользователям сразу. На своём сервере с одним-двумя слушателями это почти целиком собственная
    // ротация, и «популярное» начинает повторять историю. Свой вклад вычитается приблизительно:
    // прослушивания трека считаются недавними, если он звучал в последние 30 дней.
    private static double PopularityAmongOthers(double popularity, TrackHistory? own, DateTimeOffset now)
    {
        if (own is null || own.PlayCount <= 0 || popularity <= 0)
            return popularity;

        const double Saturation = 10;
        const int RecentDays = 30;

        var total = Saturation * popularity / Math.Max(1e-9, 1 - popularity);
        var recent = now - own.LastPlayedAt <= TimeSpan.FromDays(RecentDays) ? 2 * own.PlayCount : 0;
        var others = Math.Max(0, total - own.PlayCount - recent);

        return others / (others + Saturation);
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

[Flags]
internal enum Evidence
{
    Taste = 1,
    Playlists = 2,
    Library = 4,
    Sound = 8,
}
