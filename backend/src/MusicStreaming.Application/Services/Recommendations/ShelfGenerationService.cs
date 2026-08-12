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

/// <summary>Ключи полок, из которых собирается главная страница.</summary>
public static class ShelfKeys
{
    public const string ContinueListening = "continueListening";
    public const string ForYou = "forYou";
    public const string SimilarTo = "similarTo";
    public const string BecauseYouListened = "becauseYouListened";
    public const string Discover = "discover";
    public const string GenreMix = "genreMix";
    public const string NewReleases = "newReleases";
    public const string Popular = "popular";
    public const string ArtistsForYou = "artistsForYou";
    public const string AlbumsForYou = "albumsForYou";

    /// <summary>Собирает ключ с зерном, например <c>similarTo:1f2e…</c>.</summary>
    public static string Seeded(string key, Guid seed) => $"{key}:{seed}";

    /// <summary>Часть до зерна — именно её клиент сопоставляет с заголовком.</summary>
    public static string BaseOf(string shelfKey)
    {
        var separator = shelfKey.IndexOf(':');
        return separator < 0 ? shelfKey : shelfKey[..separator];
    }
}

/// <summary>
/// Строит все полки персональной главной одного пользователя и сохраняет результат.
///
/// <para>
/// Это дорогая половина движка, и она работает в фоне именно для того, чтобы этого не пришлось
/// делать пути чтения: к моменту прихода запроса ответ уже лежит строкой в таблице.
/// </para>
/// </summary>
public class ShelfGenerationService(
    IApplicationDbContext db,
    CandidateGenerator generator,
    IMemoryCache memoryCache,
    IOptions<RecommendationOptions> options,
    TimeProvider clock,
    RecommendationMetrics metrics,
    ILogger<ShelfGenerationService> logger)
{
    /// <summary>Ниже этого полка выглядит сломанной, поэтому её убирают, а не показывают.</summary>
    private const int MinimumShelfSize = 4;

    private const int MaxSeededShelves = 2;

    private RecommendationOptions Options => options.Value;

    /// <summary>Готовая полка, которую можно класть в кэш.</summary>
    private record Shelf(string Key, int Position, IReadOnlyList<CachedRecommendation> Items);

    /// <summary>Перестраивает и сохраняет все полки. Возвращает число рассмотренных кандидатов.</summary>
    public async Task<int> GenerateAsync(Guid userId, Guid runId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();

        var context = await generator.LoadContextAsync(userId, now, ct);
        var candidates = await generator.GenerateAsync(context, ct);

        var weights = Options.WeightsFor(context.Profile.Maturity);
        foreach (var candidate in candidates)
            CandidateScorer.Score(candidate, context.Ranking, weights, Options);

        var shelves = await BuildShelvesAsync(context, candidates, ct);
        await PersistAsync(userId, runId, shelves, now, ct);

        logger.LogDebug(
            "Built {Shelves} shelves for user {UserId} from {Candidates} candidates ({Maturity})",
            shelves.Count, userId, candidates.Count, context.Profile.Maturity);

        return candidates.Count;
    }

    private async Task<List<Shelf>> BuildShelvesAsync(
        UserRecommendationContext context,
        List<RecommendationCandidate> candidates,
        CancellationToken ct)
    {
        var shelves = new List<Shelf>();
        var position = 0;

        // Уже размещённые треки. Последующие полки их избегают, чтобы одна главная не показывала
        // одну и ту же песню трижды, — но полка, которая иначе упала бы ниже минимума, забирает их
        // обратно, ведь связная короткая полка лучше отсутствующей.
        var used = new HashSet<Guid>();

        void Add(string key, IReadOnlyList<RecommendationCandidate> picks)
        {
            if (picks.Count < MinimumShelfSize)
                return;

            shelves.Add(new Shelf(key, position++, picks.Select(ToCached).ToList()));

            foreach (var pick in picks)
                used.Add(pick.TrackId);
        }

        List<RecommendationCandidate> Pick(
            IEnumerable<RecommendationCandidate> pool, string shelfKey, double explorationRatio)
        {
            var available = pool.Where(c => !used.Contains(c.TrackId)).ToList();
            var seed = Explorer.SeedFor(context.UserId, shelfKey, context.Ranking.Now);

            var picks = Explorer.Compose(available, Options.ShelfSize, explorationRatio, Options, seed);

            // После удаления дублей осталось мало: разрешаем повторы, а не выбрасываем полку.
            if (picks.Count < MinimumShelfSize)
            {
                var wider = pool.ToList();
                picks = Explorer.Compose(wider, Options.ShelfSize, explorationRatio, Options, seed);
            }

            return picks;
        }

        // 1. На чём пользователь остановился. Без исследования: у этой полки ровно одна задача.
        var unfinished = candidates
            .Where(c => c.Source == CandidateSource.ContinueListening)
            .OrderByDescending(c => c.Score)
            .Take(Options.ShelfSize)
            .ToList();

        Add(ShelfKeys.ContinueListening, unfinished);

        // 2. Основной персональный микс.
        Add(ShelfKeys.ForYou, Pick(candidates, ShelfKeys.ForYou, Options.ExplorationRatio));

        // 3. Соседи трека, сыгранного последним.
        var similarShelf = await BuildSimilarToLastPlayedAsync(context, used, ct);
        if (similarShelf is not null)
        {
            shelves.Add(similarShelf with { Position = position++ });
            foreach (var item in similarShelf.Items)
                used.Add(item.ItemId);
        }

        // 4. По полке на любимого исполнителя — его собственные треки и соседи, к которым он привёл.
        foreach (var artist in context.Profile.TopArtists.Take(MaxSeededShelves))
        {
            var pool = candidates.Where(c =>
                c.ArtistIds.Contains(artist.Id) || c.ReasonSubjectId == artist.Id);

            var key = ShelfKeys.Seeded(ShelfKeys.BecauseYouListened, artist.Id);

            Add(key, Explain(
                Pick(pool, key, 0), ReasonKinds.BecauseYouListened, artist.Name, artist.Id));
        }

        // 5. Намеренно незнакомое. Смысл именно в исследовании, поэтому оно выкручено вверх.
        var novel = candidates.Where(c => c.IsNovel).ToList();

        Add(ShelfKeys.Discover, Explain(
            Pick(novel, ShelfKeys.Discover, Options.DiscoveryExplorationRatio), ReasonKinds.Discovery));

        // 6. По миксу на любимый жанр.
        foreach (var genre in context.Profile.TopGenres.Take(MaxSeededShelves))
        {
            var key = ShelfKeys.Seeded(ShelfKeys.GenreMix, genre.Id);
            var picks = Pick(candidates.Where(c => c.GenreId == genre.Id), key, Options.ExplorationRatio);

            Add(key, Explain(picks, ReasonKinds.FromGenreYouLike, genre.Name, genre.Id));
        }

        // 7. Самое новое в библиотеке, отранжированное по тому, насколько оно идёт этому слушателю.
        var fresh = candidates
            .Where(c => c.Freshness > 0)
            .OrderByDescending(c => c.Score * (0.5 + c.Freshness))
            .ToList();

        Add(ShelfKeys.NewReleases, Explain(
            Pick(fresh, ShelfKeys.NewReleases, 0), ReasonKinds.FreshInLibrary));

        // 8. Что играет вся библиотека.
        var popular = candidates
            .Where(c => c.Popularity > 0)
            .OrderByDescending(c => c.Popularity)
            .ToList();

        Add(ShelfKeys.Popular, Explain(
            Pick(popular, ShelfKeys.Popular, 0), ReasonKinds.Trending));

        // 9 и 10. Тот же пул, но в разрезе исполнителей и альбомов.
        AddEntityShelf(shelves, ref position, ShelfKeys.ArtistsForYou,
            AggregateBy(candidates, c => c.ArtistId, RecommendedItemKind.Artist, context));

        AddEntityShelf(shelves, ref position, ShelfKeys.AlbumsForYou,
            AggregateBy(candidates, c => c.AlbumId, RecommendedItemKind.Album, context));

        return shelves;
    }

    /// <summary>
    /// «Ещё похожее на последнее, что вы включали». Читается прямо из таблицы похожести, а не
    /// отбирается из пула кандидатов, потому что пул ограничен и самый релевантный сосед одного
    /// конкретного трека вполне может в него не попасть.
    /// </summary>
    private async Task<Shelf?> BuildSimilarToLastPlayedAsync(
        UserRecommendationContext context, HashSet<Guid> used, CancellationToken ct)
    {
        var lastPlayed = context.Ranking.History
            .Where(pair => pair.Value.Score > 0)
            .OrderByDescending(pair => pair.Value.LastPlayedAt)
            .Select(pair => (Guid?)pair.Key)
            .FirstOrDefault();

        if (lastPlayed is not { } seedId)
            return null;

        var seed = await db.Tracks.AsNoTracking()
            .Where(t => t.Id == seedId)
            .Select(t => new { t.Title })
            .FirstOrDefaultAsync(ct);

        if (seed is null)
            return null;

        var neighbours = await db.TrackSimilarities.AsNoTracking()
            .Where(s => s.TrackId == seedId)
            .OrderByDescending(s => s.Score)
            .Take(Options.ShelfSize * 2)
            .Select(s => new { s.SimilarTrackId, s.Score })
            .ToListAsync(ct);

        var unused = neighbours.Where(n => !used.Contains(n.SimilarTrackId)).ToList();

        // Правило то же, что и у прочих полок: небольшая библиотека способна исчерпать неиспользованный
        // пул, а повторить трек в другом месте страницы лучше, чем выбросить полку целиком.
        if (unused.Count < MinimumShelfSize)
            unused = neighbours;

        var items = unused
            .Take(Options.ShelfSize)
            .Select(n => new CachedRecommendation(
                n.SimilarTrackId, RecommendedItemKind.Track, n.Score,
                ReasonKinds.SimilarTo, seed.Title, seedId))
            .ToList();

        return items.Count < MinimumShelfSize
            ? null
            : new Shelf(ShelfKeys.Seeded(ShelfKeys.SimilarTo, seedId), 0, items);
    }

    private static void AddEntityShelf(
        List<Shelf> shelves,
        ref int position,
        string key,
        List<CachedRecommendation> items)
    {
        if (items.Count < MinimumShelfSize)
            return;

        shelves.Add(new Shelf(key, position++, items));
    }

    /// <summary>
    /// Сворачивает пул треков до исполнителей или альбомов. Исполнителя представляет сильнейший его
    /// трек в пуле, поэтому за рекомендованным здесь именем всегда что-то стоит.
    /// </summary>
    private List<CachedRecommendation> AggregateBy(
        List<RecommendationCandidate> candidates,
        Func<RecommendationCandidate, Guid?> selector,
        RecommendedItemKind kind,
        UserRecommendationContext context)
    {
        var grouped = new Dictionary<Guid, (double Score, string Reason, string? Subject, Guid? SubjectId)>();

        foreach (var candidate in candidates)
        {
            if (selector(candidate) is not { } id || id == Guid.Empty)
                continue;

            if (!grouped.TryGetValue(id, out var existing) || candidate.Score > existing.Score)
            {
                grouped[id] = (
                    candidate.Score,
                    candidate.ReasonKind,
                    candidate.ReasonSubject,
                    candidate.ReasonSubjectId);
            }
        }

        // Исполнитель, которого пользователь и так крутит без остановки, — не рекомендация. Его
        // устоявшиеся любимцы убираются, чтобы полка сказала то, чего он ещё не знает.
        var establishedArtists = context.Profile.TopArtists.Take(3).Select(a => a.Id).ToHashSet();

        return grouped
            .Where(pair => kind != RecommendedItemKind.Artist || !establishedArtists.Contains(pair.Key))
            .OrderByDescending(pair => pair.Value.Score)
            .Take(Options.ShelfSize)
            .Select(pair => new CachedRecommendation(
                pair.Key, kind, pair.Value.Score,
                pair.Value.Reason, pair.Value.Subject, pair.Value.SubjectId))
            .ToList();
    }

    /// <summary>
    /// Перебивает пояснения отдельных кандидатов на полке, чей заголовок уже называет причину.
    /// «Новое в вашей библиотеке» не должно числиться под жанром, через который случайно нашёлся
    /// один из её треков.
    /// </summary>
    private static List<RecommendationCandidate> Explain(
        List<RecommendationCandidate> picks, string reasonKind, string? subject = null, Guid? subjectId = null)
    {
        foreach (var pick in picks)
        {
            pick.ReasonKind = reasonKind;
            pick.ReasonSubject = subject;
            pick.ReasonSubjectId = subjectId;
        }

        return picks;
    }

    private static CachedRecommendation ToCached(RecommendationCandidate candidate) => new(
        candidate.TrackId,
        RecommendedItemKind.Track,
        candidate.Score,
        candidate.ReasonKind,
        candidate.ReasonSubject,
        candidate.ReasonSubjectId);

    /// <summary>
    /// Заменяет закэшированные полки пользователя и записывает, что было показано.
    ///
    /// <para>
    /// Показы пишутся здесь, во время генерации, а не при отдаче полки. Путь чтения остаётся только
    /// на чтение, а пауза перед повтором работает по тому, что действительно положили перед
    /// пользователем, — а это и есть сгенерированная полка.
    /// </para>
    /// </summary>
    private async Task PersistAsync(
        Guid userId, Guid runId, List<Shelf> shelves, DateTimeOffset now, CancellationToken ct)
    {
        var expiresAt = now.AddHours(Options.CacheTtlHours);

        var existing = await db.RecommendationCache
            .Where(c => c.UserId == userId)
            .ToListAsync(ct);

        var byKey = existing.ToDictionary(c => c.ShelfKey);

        foreach (var shelf in shelves)
        {
            if (byKey.Remove(shelf.Key, out var entry))
            {
                entry.Payload = shelf.Items;
                entry.Position = shelf.Position;
                entry.GeneratedAt = now;
                entry.ExpiresAt = expiresAt;
                entry.RunId = runId;
            }
            else
            {
                db.RecommendationCache.Add(new RecommendationCacheEntry
                {
                    UserId = userId,
                    ShelfKey = shelf.Key,
                    Position = shelf.Position,
                    Payload = shelf.Items,
                    GeneratedAt = now,
                    ExpiresAt = expiresAt,
                    RunId = runId,
                });
            }

            RecordImpressions(userId, shelf, now);
        }

        // Полки, потерявшие смысл: жанр, вышедший из милости, удалённый трек-зерно.
        db.RecommendationCache.RemoveRange(byKey.Values);

        await db.SaveChangesAsync(ct);

        // Иначе внутрипроцессный кэш перед этими строками продолжал бы отдавать прежние полки весь
        // свой срок жизни, и перестроение, вызванное только что сделанным пользователем действием,
        // выглядело бы как не давшее ничего.
        memoryCache.Remove(RecommendationCacheKeys.Shelves(userId));
    }

    private void RecordImpressions(Guid userId, Shelf shelf, DateTimeOffset now)
    {
        var position = 0;

        foreach (var item in shelf.Items)
        {
            if (item.Kind != RecommendedItemKind.Track)
                continue;

            db.RecommendationImpressions.Add(new RecommendationImpression
            {
                UserId = userId,
                TrackId = item.ItemId,
                ShelfKey = shelf.Key,
                Position = position++,
                ShownAt = now,
            });
        }

        metrics.RecordImpressions(position, ShelfKeys.BaseOf(shelf.Key));
    }
}
