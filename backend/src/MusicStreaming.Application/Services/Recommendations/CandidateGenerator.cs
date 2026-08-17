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

/// <summary>Всё о пользователе, что нужно и генерации кандидатов, и ранжированию.</summary>
/// <param name="UserId">Пользователь, для которого строится рекомендация.</param>
/// <param name="Profile">Сохранённый ролл-ап вкуса пользователя (годовые предпочтения, счётчик сигналов).</param>
/// <param name="Ranking">Аффинити-словари и история, нужные ранжированию для оценки и штрафов.</param>
/// <param name="SeedTrackIds">Недавно понравившиеся треки — точка отсчёта для источников "похожее на недавнее" и "из тех же плейлистов".</param>
/// <param name="GenreShare">Доля каждого жанра в библиотеке — используется для расчёта охвата на холодном старте.</param>
public record UserRecommendationContext(
    Guid UserId,
    UserTasteProfile Profile,
    RankingContext Ranking,
    IReadOnlyList<Guid> SeedTrackIds,
    IReadOnlyDictionary<Guid, double> GenreShare)
{
    /// <summary>Истина, когда у пользователя ещё нет ни одного положительного сигнала — тогда ранжирование опирается на популярность и охват, а не на персональные аффинити.</summary>
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
    IMemoryCache memoryCache,
    IOptions<RecommendationOptions> options,
    ILogger<CandidateGenerator> logger)
{
    private const int SeedTrackCount = 20;
    private const int TopArtistCount = 8;
    private const int TopGenreCount = 4;
    private const int NeighbourCount = 20;
    private const int MinimumNeighbourOverlap = 3;

    /// <summary>Ниже этого пул радио слишком узок, чтобы после исключений и квот разнообразия из него что-то осталось.</summary>
    private const int RadioPoolFloor = 40;

    /// <summary>
    /// Сколько живёт кэш долей жанров. Долго по меркам запроса и коротко по меркам библиотеки:
    /// величина меняется только при загрузке и удалении треков, а используется лишь для расчёта
    /// охвата на холодном старте, где отставание на минуту ничего не решает.
    /// </summary>
    private static readonly TimeSpan GenreShareLifetime = TimeSpan.FromMinutes(5);

    private RecommendationOptions Options => options.Value;

    /// <summary>
    /// Заявка одного источника об одном треке — промежуточное представление до того, как трек
    /// обогащён метаданными и превращён в полноценного <see cref="RecommendationCandidate"/>.
    /// </summary>
    /// <param name="TrackId">Заявленный трек.</param>
    /// <param name="Source">Источник, сделавший заявку.</param>
    /// <param name="Content">Вклад в похожесть по метаданным, если источник его определяет.</param>
    /// <param name="Collaborative">Вклад в коллаборативную похожесть, если источник его определяет.</param>
    /// <param name="Popularity">Вклад в общую популярность, если источник его определяет.</param>
    /// <param name="ReasonKind">Код причины рекомендации для этого трека.</param>
    /// <param name="ReasonSubject">Имя объекта, подставляемое в формулировку причины.</param>
    /// <param name="ReasonSubjectId">Идентификатор объекта из <paramref name="ReasonSubject"/>.</param>
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
    /// <param name="userId">Пользователь, для которого собирается контекст.</param>
    /// <param name="now">Текущий момент — точка отсчёта для окна показов и для расчётов недавности.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Заполненный <see cref="UserRecommendationContext"/>, готовый и для генерации кандидатов, и для их ранжирования.</returns>
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
    /// <param name="context">Контекст пользователя, полученный из <see cref="LoadContextAsync"/>.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Объединённый пул кандидатов со всеми базовыми сигналами (Content, Collaborative, Popularity, Freshness, Coverage), но ещё без поведенческой оценки и штрафов — их считает <see cref="CandidateScorer"/>.</returns>
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
    /// Пул для радио: то, что естественно продолжает один конкретный трек.
    ///
    /// <para>
    /// Отдельный вход, а не <see cref="GenerateAsync"/>, потому что задача другая. Полкам нужен
    /// широкий охват вкуса, и девять источников с их шестью сотнями кандидатов оправданы раз в
    /// несколько часов в фоне. Радио же продолжает конкретную песню каждые несколько треков, и ему
    /// нужны соседи именно этой песни. Кандидаты при этом строятся тем же кодом и получают те же
    /// сигналы, поэтому оценивает их тот же ранкер.
    /// </para>
    /// </summary>
    /// <param name="context">Контекст пользователя из <see cref="LoadContextAsync"/>.</param>
    /// <param name="seedTrackId">Трек, который только что играл.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Кандидаты со всеми базовыми сигналами, ещё не оценённые.</returns>
    public async Task<List<RecommendationCandidate>> AroundAsync(
        UserRecommendationContext context, Guid seedTrackId, CancellationToken ct = default)
    {
        var hits = new Dictionary<Guid, Hit>();
        Merge(hits, await NeighboursOfAsync(seedTrackId, ct));

        // У только что загруженного трека соседей ещё нет, а музыка играть должна уже сейчас.
        if (hits.Count < RadioPoolFloor)
        {
            var related = await SameArtistOrGenreAsync(seedTrackId, Options.PerSourceLimit, ct);

            Merge(hits, related.Select(id => new Hit(
                id, CandidateSource.SimilarToRecent, Content: 0.5, ReasonKind: ReasonKinds.SimilarTo)));
        }

        // И совсем пустая библиотека без похожести хотя бы продолжит тем, что в ней слушают.
        if (hits.Count < RadioPoolFloor)
            Merge(hits, await PopularAsync(ct));

        hits.Remove(seedTrackId);

        return await MaterialiseAsync(hits, context, ct);
    }

    /// <summary>Соседи трека из предрассчитанной таблицы похожести.</summary>
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

    /// <summary>
    /// Треки того же исполнителя или жанра — чем подменяется похожесть, пока её не посчитали.
    /// Общее для радио и для выдачи «похожих треков»: пустой ответ читался бы как «ни на что не
    /// похоже», что почти никогда не правда.
    /// </summary>
    /// <param name="seedTrackId">Трек-затравка.</param>
    /// <param name="limit">Сколько треков вернуть.</param>
    /// <param name="ct">Токен отмены.</param>
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

    /// <summary>
    /// Сохраняет сильнейшее свидетельство для трека, найденного несколькими источниками, и
    /// объяснение того источника, который заявил его первым.
    /// </summary>
    /// <param name="pool">Накопленный по всем предыдущим источникам пул заявок — дополняется на месте.</param>
    /// <param name="produced">Заявки, полученные от очередного источника.</param>
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
    /// <param name="hits">Слитые заявки источников, по одной на трек.</param>
    /// <param name="context">Контекст пользователя — источник аффинити для расчёта новизны и охвата.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Полностью заполненные кандидаты с метаданными трека, свежестью, охватом и признаком новизны.</returns>
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
    /// <returns>Заявки с полной уверенностью в содержательном сходстве (Content = 1), так как это буквально тот же трек, который пользователь уже начал слушать.</returns>
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
    /// <returns>Заявки на треки, похожие на недавние seed-треки, с формулировкой причины в зависимости от того, чем вызвана похожесть — авторством или совстречаемостью.</returns>
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
    /// <returns>Заявки на треки топовых по аффинити исполнителей пользователя.</returns>
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
    /// <returns>Заявки на треки, которые любят пользователи с похожим набором лайков; пустой список, пока установка не набрала достаточно пользователей и пересечений.</returns>
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
    /// <returns>Заявки на треки топовых по аффинити жанров пользователя.</returns>
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
    /// <returns>Заявки на треки из плейлистов, которые уже содержат один из seed-треков пользователя, — куратор задаёт связь, которую трудно вывести из метаданных.</returns>
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
    /// <returns>Заявки на самые новые треки библиотеки, с разной формулировкой причины в зависимости от того, знаком ли пользователю исполнитель.</returns>
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
    /// <returns>Заявки на самые популярные треки библиотеки — сигнал, не зависящий от конкретного пользователя, поэтому источник не принимает <see cref="UserRecommendationContext"/>.</returns>
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
    /// <returns>Заявки на треки, у которых нет вообще никакого аффинити с пользователем — пул, из которого черпает исследовательская часть полки на холодном старте.</returns>
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
    /// <param name="genreId">Жанр трека-кандидата; <c>null</c> получает нейтральное среднее значение.</param>
    /// <param name="context">Источник долей жанров в библиотеке (<see cref="UserRecommendationContext.GenreShare"/>).</param>
    /// <returns>1 минус доля жанра в библиотеке — редкий жанр даёт значение, близкое к 1, доминирующий — близкое к 0.</returns>
    private static double CoverageFor(Guid? genreId, UserRecommendationContext context)
    {
        if (genreId is not { } id)
            return 0.5;

        return context.GenreShare.TryGetValue(id, out var share) ? 1 - share : 1;
    }

    /// <summary>
    /// Считает долю каждого жанра среди всех треков библиотеки — основа для расчёта охвата на
    /// холодном старте.
    ///
    /// <para>
    /// Кэшируется, потому что это группировка по всей таблице треков, а спрашивают её на каждом
    /// запросе радио и на каждой генерации полок. Ответ у всех пользователей один и тот же —
    /// свойство библиотеки, а не человека, — поэтому и ключ кэша общий.
    /// </para>
    /// </summary>
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

    /// <summary>Топ-N ключей словаря аффинити по убыванию положительной оценки; отрицательные и нулевые значения исключаются — интересуют только настоящие предпочтения, а не безразличие или неприязнь.</summary>
    private static List<Guid> TopScoring(IReadOnlyDictionary<Guid, double> scores, int count) =>
        scores
            .Where(pair => pair.Value > 0)
            .OrderByDescending(pair => pair.Value)
            .Take(count)
            .Select(pair => pair.Key)
            .ToList();
}
