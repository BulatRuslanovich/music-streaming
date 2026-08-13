using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Application.Recommendations.Scoring;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

/// <summary>Всё о пользователе, что нужно и генерации кандидатов, и ранжированию.</summary>
public record UserRecommendationContext(
    Guid UserId,
    UserTasteProfile Profile,
    RankingContext Ranking,
    IReadOnlyList<Guid> SeedTrackIds,
    IReadOnlyDictionary<Guid, double> GenreShare)
{
    public bool IsColdStart => Profile.PositiveSignalCount == 0;
}

/// <summary>
/// Формирует пул, из которого выбирает ранжирование.
///
/// <para>
/// Хорошие рекомендации — сначала задача полноты и только потом точности: ранкер способен выбрать
/// лишь из того, что ему дали. Поэтому девять независимых источников приносят каждый своё —
/// соседей только что сыгранного, ещё треки любимых исполнителей, то, что слушают похожие
/// слушатели, новое, популярное и вовсе ни разу не слышанное. Каждый источник ограничен, так что
/// ни один не вытеснит остальные, а трек, найденный несколькими источниками, сохраняет сильнейшее
/// свидетельство от каждого вместо того, чтобы появиться несколько раз.
/// </para>
///
/// <para>
/// Источники намеренно не отфильтровывают уже прослушанные треки. Повторы — забота ранжирования,
/// ими занимаются штрафы остывания, а фильтрация здесь сделала бы невыразимой полку «продолжить
/// прослушивание».
/// </para>
///
/// <para>
/// Каждый источник возвращает только идентификаторы треков и те сигналы, за которые отвечает.
/// Метаданные треков затем грузятся один раз для объединённого пула — так запросы остаются
/// небольшими, а свойства, общие для всех кандидатов (свежесть, охват жанров, новизна),
/// вычисляются ровно в одном месте.
/// </para>
/// </summary>
public class CandidateGenerator(
    IApplicationDbContext db,
    IOptions<RecommendationOptions> options,
    ILogger<CandidateGenerator> logger)
{
    private const int SeedTrackCount = 20;
    private const int TopArtistCount = 8;
    private const int TopGenreCount = 4;
    private const int NeighbourCount = 20;
    private const int MinimumNeighbourOverlap = 3;

    private RecommendationOptions Options => options.Value;

    /// <summary>Заявка одного источника об одном треке.</summary>
    private record Hit(
        Guid TrackId,
        CandidateSource Source,
        double Content = 0,
        double Collaborative = 0,
        double Popularity = 0,
        string ReasonKind = ReasonKinds.Discovery,
        string? ReasonSubject = null,
        Guid? ReasonSubjectId = null);

    /// <summary>Загружает всё, что ранжированию нужно о пользователе, в одном месте.</summary>
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

    /// <summary>Запускает все источники и сливает результат в один готовый к оценке пул.</summary>
    public async Task<List<RecommendationCandidate>> GenerateAsync(
        UserRecommendationContext context, CancellationToken ct = default)
    {
        var hits = new Dictionary<Guid, Hit>();

        // Порядок — по тому, насколько убедительно читается получающееся объяснение: причину,
        // которую увидит пользователь, даёт источник, первым заявивший трек.
        Merge(hits, await ContinueListeningAsync(context, ct));
        Merge(hits, await SimilarToRecentAsync(context, ct));
        Merge(hits, await FromLovedArtistsAsync(context, ct));
        Merge(hits, await FromSimilarListenersAsync(context, ct));
        Merge(hits, await FromLovedGenresAsync(context, ct));
        Merge(hits, await FromSharedPlaylistsAsync(context, ct));
        Merge(hits, await NewReleasesAsync(context, ct));
        Merge(hits, await PopularAsync(ct));
        Merge(hits, await UnheardAsync(context, ct));

        var candidates = await MaterialiseAsync(hits, context, ct);

        logger.LogDebug(
            "Generated {Count} candidates for user {UserId} ({Mode})",
            candidates.Count, context.UserId, context.IsColdStart ? "cold start" : "personalised");

        return candidates;
    }

    /// <summary>
    /// Сохраняет сильнейшее свидетельство для трека, найденного несколькими источниками, и
    /// объяснение того источника, который заявил его первым.
    /// </summary>
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

    /// <summary>
    /// Превращает слитые заявки в кандидатов: один запрос метаданных, затем сигналы, зависящие
    /// только от самого трека.
    /// </summary>
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

            // Новизна определяется намеренно щедро: и ни разу не игранный исполнитель, и жанр вне
            // первой тройки считаются направлением, куда можно пойти. Из этого пула черпает исследование.
            var knownArtist = credits.Any(id =>
                context.Ranking.ArtistScores.TryGetValue(id, out var score) && score > 0);
            var knownGenre = row.GenreId is { } genreId && topGenres.Contains(genreId);

            candidate.IsNovel = !context.Ranking.History.ContainsKey(row.Id) && (!knownArtist || !knownGenre);

            candidates.Add(candidate);
        }

        return candidates;
    }

    // ── Источники ───────────────────────────────────────────────────────────────────────────

    /// <summary>Треки, недавно начатые и не досмотренные до конца, — там, где пользователь остановился.</summary>
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

    /// <summary>Соседи того, что пользователю недавно понравилось, — основной персональный источник.</summary>
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
            // Назвать исполнителя читается лучше, чем назвать трек, — кроме случая, когда связь
            // возникла из совместного прослушивания, а не из авторства: тогда честнее «похоже на X».
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

    /// <summary>Ещё треки исполнителей, которых пользователь действительно слушает.</summary>
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

    /// <summary>
    /// То, что слушают люди с пересекающимся вкусом.
    ///
    /// <para>
    /// Ниже настроенных порогов выключено — и для маленькой установки это честное поведение по
    /// умолчанию: при двух-трёх учётных записях «слушатели вроде вас» описывает одного человека, и
    /// выдавать это за коллаборативную фильтрацию было бы театром. До тех пор коллаборативный
    /// сигнал в одиночку несёт item-item похожесть, построенная на сессиях прослушивания; как
    /// только установка перерастёт пороги, источник начнёт вносить вклад сам — без миграции и без
    /// смены формы данных.
    /// </para>
    /// </summary>
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

    /// <summary>Треки из жанров, к которым пользователь тяготеет.</summary>
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

    /// <summary>Треки, соседствующие с избранным пользователя в чьём-нибудь плейлисте.</summary>
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

    /// <summary>То, что попало в библиотеку позже всего.</summary>
    private async Task<List<Hit>> NewReleasesAsync(UserRecommendationContext context, CancellationToken ct)
    {
        var rows = await db.Tracks.AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .Take(Options.PerSourceLimit)
            .Select(t => new { t.Id, t.ArtistId, ArtistName = t.Artist!.Name })
            .ToListAsync(ct);

        return rows.Select(row =>
        {
            // Новая загрузка того, кого слушатель уже слушает, — самая сильная из новостей, и
            // она заслуживает, чтобы об этом сказали прямо.
            var known = context.Ranking.ArtistScores.TryGetValue(row.ArtistId, out var score) && score > 0;

            return known
                ? new Hit(row.Id, CandidateSource.NewReleases,
                    ReasonKind: ReasonKinds.NewFromArtistYouPlay,
                    ReasonSubject: row.ArtistName, ReasonSubjectId: row.ArtistId)
                : new Hit(row.Id, CandidateSource.NewReleases, ReasonKind: ReasonKinds.FreshInLibrary);
        }).ToList();
    }

    /// <summary>То, что слушает библиотека в целом.</summary>
    private async Task<List<Hit>> PopularAsync(CancellationToken ct)
    {
        var rows = await db.TrackStats.AsNoTracking()
            .Where(s => s.PopularityScore > 0)
            .OrderByDescending(s => s.PopularityScore)
            .Take(Options.PerSourceLimit)
            .Select(s => new { s.TrackId, s.PopularityScore })
            .ToListAsync(ct);

        return rows.Select(row => new Hit(
            row.TrackId, CandidateSource.Popular,
            Popularity: row.PopularityScore,
            ReasonKind: ReasonKinds.Trending)).ToList();
    }

    /// <summary>
    /// Треки, которые пользователь ни разу не включал, — пул исследования. На холодном старте,
    /// когда все персональные источники пусты, это почти вся библиотека.
    /// </summary>
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

    // ── Общие вспомогательные методы ────────────────────────────────────────────────────────

    /// <summary>
    /// Насколько трек расширяет полку холодного старта. Редкие жанры получают больше, чем тот
    /// жанр, который доминирует в библиотеке, — поэтому первый визит показывает срез коллекции, а
    /// не её зеркало.
    /// </summary>
    private static double CoverageFor(Guid? genreId, UserRecommendationContext context)
    {
        if (genreId is not { } id)
            return 0.5;

        return context.GenreShare.TryGetValue(id, out var share) ? 1 - share : 1;
    }

    private async Task<Dictionary<Guid, double>> LoadGenreShareAsync(CancellationToken ct)
    {
        var counts = await db.Tracks.AsNoTracking()
            .Where(t => t.GenreId != null)
            .GroupBy(t => t.GenreId!.Value)
            .Select(g => new { GenreId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var total = counts.Sum(c => c.Count);
        return total == 0
            ? []
            : counts.ToDictionary(c => c.GenreId, c => (double)c.Count / total);
    }

    private static List<Guid> TopScoring(IReadOnlyDictionary<Guid, double> scores, int count) =>
        scores
            .Where(pair => pair.Value > 0)
            .OrderByDescending(pair => pair.Value)
            .Take(count)
            .Select(pair => pair.Key)
            .ToList();
}
