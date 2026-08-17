using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

/// <summary>
/// Путь чтения.
///
/// <para>
/// Всё дорогое уже случилось в фоне, поэтому отдать полку — это чтение закэшированных идентификаторов
/// по первичному ключу плюс один запрос наполнения на каждый вид элементов. Кэшируются только
/// идентификаторы: названия, признаки обложек и отметка «избранное» читаются заново каждый раз,
/// поэтому переименование трека или лайк видны сразу, а не после следующего прохода генерации.
/// </para>
/// </summary>
public class RecommendationService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ShelfGenerationService generation,
    CandidateGenerator generator,
    RecommendationRefreshQueue refreshQueue,
    IMemoryCache memoryCache,
    IOptions<RecommendationOptions> options,
    TimeProvider clock,
    RecommendationMetrics metrics,
    ILogger<RecommendationService> logger)
{
    /// <summary>
    /// Как долго идентификаторы полок держатся в процессе. Недолго: это нужно, чтобы поглотить
    /// всплеск запросов от загрузки страницы, а не чтобы стать вторым ярусом кэша.
    /// </summary>
    private static readonly TimeSpan MemoryCacheLifetime = TimeSpan.FromSeconds(60);

    /// <summary>
    /// По одной синхронной генерации на пользователя за раз.
    ///
    /// <para>
    /// Первый визит строит полки прямо в запросе, и это секунды. Кэш в памяти заполняется только
    /// по её окончании, поэтому второй запрос того же человека — обновлённая страница, вторая
    /// вкладка — снова видел промах и запускал вторую генерацию. Обе доходили до записи кэша,
    /// обе вставляли одни и те же <c>(UserId, ShelfKey)</c>, и проигравшая падала на первичном
    /// ключе — то есть человек получал пустую главную ровно там, где всё и затевалось.
    /// </para>
    ///
    /// <para>
    /// Замок в памяти процесса, а не в базе: второй копии сервиса в этой поставке нет, а
    /// распределённая блокировка ради неё стоила бы дороже самой задачи.
    /// </para>
    /// </summary>
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> InlineBuilds = new();

    private RecommendationOptions Options => options.Value;

    /// <summary>Собирает персональную главную страницу.</summary>
    /// <param name="sectionSize">Сколько элементов показывать в каждой секции; зажимается до <c>Options.ShelfSize</c>.</param>
    /// <param name="includeScores">Включать ли в ответ сырые оценки — используется отладочным/админским клиентом, не мобильным приложением.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Секции для главной страницы текущего пользователя; пустой список с <c>IsColdStart = true</c>, если полок ещё нет вовсе.</returns>
    public async Task<RecommendationHomeDto> GetHomeAsync(
        int sectionSize, bool includeScores = false, CancellationToken ct = default)
    {
        metrics.RecordRequest("home");

        var userId = currentUser.Id;
        var shelves = await LoadShelvesAsync(userId, ct);

        if (shelves.Count == 0)
            return new RecommendationHomeDto([], IsColdStart: true, GeneratedAt: null);

        var size = Math.Clamp(sectionSize, 1, Options.ShelfSize);
        var sections = await HydrateAsync(userId, shelves, size, includeScores, ct);

        var profile = await db.UserTasteProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        return new RecommendationHomeDto(
            sections,
            profile is null || profile.PositiveSignalCount == 0,
            shelves.Max(s => s.GeneratedAt));
    }

    /// <summary>
    /// Персональная лента треков, постранично.
    ///
    /// <para>
    /// Берётся из всех закэшированных полок с треками, а не из одной: полки уже представляют собой
    /// отранжированный, очищенный от дублей и разнообразный результат, и слияние их по оценке даёт
    /// более длинную ленту без второй стратегии генерации, способной разойтись с первой.
    /// </para>
    /// </summary>
    /// <param name="page">Номер и размер страницы.</param>
    /// <param name="includeScores">Включать ли сырые оценки в ответ.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Страница объединённой и переранжированной ленты треков со всех полок пользователя.</returns>
    public async Task<PagedResult<RecommendedTrackDto>> GetTracksAsync(
        PageRequest page, bool includeScores = false, CancellationToken ct = default)
    {
        metrics.RecordRequest("tracks");

        var userId = currentUser.Id;
        var shelves = await LoadShelvesAsync(userId, ct);

        var ranked = shelves
            .SelectMany(shelf => shelf.Payload)
            .Where(item => item.Kind == RecommendedItemKind.Track)
            .GroupBy(item => item.ItemId)
            .Select(group => group.OrderByDescending(item => item.Score).First())
            .OrderByDescending(item => item.Score)
            .ToList();

        var pageItems = ranked.Skip(page.Skip).Take(page.PageSize).ToList();
        var tracks = await db.TracksByIdAsync(userId, pageItems.Select(i => i.ItemId), ct);

        var items = pageItems
            .Where(item => tracks.ContainsKey(item.ItemId))
            .Select(item => ToDto(tracks[item.ItemId], item, includeScores))
            .ToList();

        return new PagedResult<RecommendedTrackDto>(items, ranked.Count, page.Page, page.PageSize);
    }

    /// <summary>Рекомендованные исполнители.</summary>
    /// <param name="limit">Максимум исполнителей в ответе.</param>
    /// <param name="ct">Токен отмены.</param>
    public async Task<IReadOnlyList<ArtistDto>> GetArtistsAsync(int limit, CancellationToken ct = default)
    {
        metrics.RecordRequest("artists");
        return await GetEntitiesAsync(
            ShelfKeys.ArtistsForYou, RecommendedItemKind.Artist, limit, db.ArtistsByIdAsync, ct);
    }

    /// <summary>Рекомендованные альбомы.</summary>
    /// <param name="limit">Максимум альбомов в ответе.</param>
    /// <param name="ct">Токен отмены.</param>
    public async Task<IReadOnlyList<AlbumDto>> GetAlbumsAsync(int limit, CancellationToken ct = default)
    {
        metrics.RecordRequest("albums");
        return await GetEntitiesAsync(
            ShelfKeys.AlbumsForYou, RecommendedItemKind.Album, limit, db.AlbumsByIdAsync, ct);
    }

    /// <summary>
    /// Треки, похожие на заданный.
    ///
    /// <para>
    /// Откатывается к собственному исполнителю и жанру трека, когда в таблице похожести для него
    /// ничего нет, — обычное состояние трека, загруженного пять минут назад, и вообще любого трека до
    /// первого прохода обслуживания. Пустой список читался бы как «ничего на это не похоже», что
    /// почти никогда не правда.
    /// </para>
    /// </summary>
    /// <param name="trackId">Трек, для которого ищутся похожие.</param>
    /// <param name="limit">Максимум треков в ответе.</param>
    /// <param name="includeScores">Включать ли сырые оценки похожести в ответ.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Похожие треки в порядке убывания оценки; при пустой таблице похожести — откат на исполнителя/жанр того же трека.</returns>
    /// <exception cref="NotFoundException">Трек с <paramref name="trackId"/> не найден.</exception>
    public async Task<IReadOnlyList<RecommendedTrackDto>> GetSimilarAsync(
        Guid trackId, int limit, bool includeScores = false, CancellationToken ct = default)
    {
        metrics.RecordRequest("similar");

        var seed = await db.Tracks.AsNoTracking()
            .Where(t => t.Id == trackId)
            .Select(t => new { t.Id, t.Title, t.ArtistId, t.GenreId })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Track not found.");

        var size = Math.Clamp(limit, 1, PageRequest.MaxPageSize);

        var neighbours = await db.TrackSimilarities.AsNoTracking()
            .Where(s => s.TrackId == trackId)
            .OrderByDescending(s => s.Score)
            .Take(size)
            .Select(s => new { s.SimilarTrackId, s.Score })
            .ToListAsync(ct);

        var scores = neighbours.ToDictionary(n => n.SimilarTrackId, n => n.Score);
        var order = neighbours.Select(n => n.SimilarTrackId).ToList();

        if (order.Count == 0)
            order = [.. await generator.SameArtistOrGenreAsync(trackId, size, ct)];

        var tracks = await db.TracksByIdAsync(currentUser.Id, order, ct);
        var reason = new RecommendationReasonDto(ReasonKinds.SimilarTo, seed.Title, seed.Id);

        return order
            .Where(tracks.ContainsKey)
            .Select(id => new RecommendedTrackDto(
                tracks[id], reason, includeScores ? scores.GetValueOrDefault(id) : null))
            .ToList();
    }

    // ── Загрузка полок ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Читает закэшированные полки пользователя, генерируя их на месте, только если их нет совсем.
    ///
    /// <para>
    /// Протухшие полки отдаются как есть, а перестроение ставится в очередь. Несвежие рекомендации —
    /// куда меньшая беда, чем главная страница, замирающая на секунду в раздумье.
    /// </para>
    /// </summary>
    /// <param name="userId">Пользователь, чьи полки читаются.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Список закэшированных записей полок; пуст только если фоновая генерация ещё ни разу не запускалась и инлайн-генерация тоже не удалась.</returns>
    private async Task<List<RecommendationCacheEntry>> LoadShelvesAsync(Guid userId, CancellationToken ct)
    {
        var cacheKey = RecommendationCacheKeys.Shelves(userId);

        if (memoryCache.TryGetValue(cacheKey, out List<RecommendationCacheEntry>? cached) && cached is not null)
        {
            metrics.RecordCacheHit("memory");
            return cached;
        }

        var shelves = await ReadShelvesAsync(userId, ct);

        if (shelves.Count == 0)
        {
            metrics.RecordCacheMiss("empty");
            shelves = await BuildOnceAsync(userId, ct);
        }
        else
        {
            var now = clock.GetUtcNow();
            metrics.RecordCacheHit("database");

            if (shelves.Any(s => s.ExpiresAt <= now))
                refreshQueue.MarkDirty(userId, now);
        }

        memoryCache.Set(cacheKey, shelves, MemoryCacheLifetime);
        return shelves;
    }

    /// <summary>Прямое чтение полок из базы, в обход памяти процесса — источник истины для <see cref="LoadShelvesAsync"/>.</summary>
    private async Task<List<RecommendationCacheEntry>> ReadShelvesAsync(Guid userId, CancellationToken ct) =>
        await db.RecommendationCache.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Position)
            .ToListAsync(ct);

    /// <summary>
    /// Синхронная генерация под замком: пока её делает один запрос, остальные ждут и забирают
    /// готовое, вместо того чтобы строить то же самое второй раз и спорить за строки кэша.
    /// </summary>
    private async Task<List<RecommendationCacheEntry>> BuildOnceAsync(Guid userId, CancellationToken ct)
    {
        var gate = InlineBuilds.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(ct);
        try
        {
            // Победитель мог всё построить, пока мы стояли в очереди.
            var built = await ReadShelvesAsync(userId, ct);
            if (built.Count > 0)
            {
                metrics.RecordCacheHit("database");
                return built;
            }

            return await GenerateInlineAsync(userId, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Строит полки прямо во время запроса. Сюда попадают только при самом первом визите
    /// пользователя или после очистки кэша — во всех остальных случаях отдаёт фоновый проход.
    /// </summary>
    /// <param name="userId">Пользователь, для которого генерируются полки прямо сейчас.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Только что построенные полки; пустой список, если генерация упала — так вызывающий код может отдать пустую, но рабочую страницу вместо ошибки.</returns>
    private async Task<List<RecommendationCacheEntry>> GenerateInlineAsync(Guid userId, CancellationToken ct)
    {
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var run = new RecommendationRun
        {
            UserId = userId,
            Trigger = RecommendationTrigger.OnDemand,
            StartedAt = clock.GetUtcNow(),
            Status = RecommendationRunStatus.Succeeded,
        };

        try
        {
            run.CandidateCount = await generation.GenerateAsync(userId, run.Id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Слушатель, запросивший свою главную, должен получить остальную страницу, а не 500.
            logger.LogError(ex, "Inline recommendation generation failed for user {UserId}", userId);
            return [];
        }

        var shelves = await ReadShelvesAsync(userId, ct);

        run.ShelfCount = shelves.Count;
        run.DurationMs = (int)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

        db.RecommendationRuns.Add(run);
        await db.SaveChangesAsync(ct);

        metrics.RecordGeneration(
            System.Diagnostics.Stopwatch.GetElapsedTime(startedAt), run.CandidateCount);

        return shelves;
    }

    // ── Наполнение ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Превращает закэшированные идентификаторы полок в полноценные секции с названиями,
    /// обложками и метаданными — тремя запросами (треки/исполнители/альбомы) на всю страницу
    /// разом, а не по одному на полку.
    /// </summary>
    /// <param name="userId">Пользователь — нужен для персональных полей трека вроде отметки "избранное".</param>
    /// <param name="shelves">Закэшированные полки, чьи идентификаторы наполняются данными.</param>
    /// <param name="sectionSize">Сколько элементов брать из каждой полки.</param>
    /// <param name="includeScores">Включать ли сырые оценки в ответ.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Секции для главной страницы; полки, опустевшие после генерации (треки успели удалить), пропускаются.</returns>
    private async Task<List<RecommendationSectionDto>> HydrateAsync(
        Guid userId,
        List<RecommendationCacheEntry> shelves,
        int sectionSize,
        bool includeScores,
        CancellationToken ct)
    {
        var wanted = shelves
            .SelectMany(shelf => shelf.Payload.Take(sectionSize).Select(item => (shelf, item)))
            .ToList();

        var tracks = await db.TracksByIdAsync(userId, Ids(wanted, RecommendedItemKind.Track), ct);
        var artists = await db.ArtistsByIdAsync(Ids(wanted, RecommendedItemKind.Artist), ct);
        var albums = await db.AlbumsByIdAsync(Ids(wanted, RecommendedItemKind.Album), ct);

        var sections = new List<RecommendationSectionDto>(shelves.Count);

        foreach (var shelf in shelves)
        {
            var items = shelf.Payload.Take(sectionSize).ToList();
            if (items.Count == 0)
                continue;

            var reason = ReasonOf(items[0]);
            var section = items[0].Kind switch
            {
                RecommendedItemKind.Artist => new RecommendationSectionDto(
                    shelf.ShelfKey, ShelfKeys.BaseOf(shelf.ShelfKey), reason, null,
                    Resolve(items, artists), null),

                RecommendedItemKind.Album => new RecommendationSectionDto(
                    shelf.ShelfKey, ShelfKeys.BaseOf(shelf.ShelfKey), reason, null, null,
                    Resolve(items, albums)),

                _ => new RecommendationSectionDto(
                    shelf.ShelfKey, ShelfKeys.BaseOf(shelf.ShelfKey), reason,
                    items.Where(item => tracks.ContainsKey(item.ItemId))
                        .Select(item => ToDto(tracks[item.ItemId], item, includeScores))
                        .ToList(),
                    null, null),
            };

            // Полка может опустеть между генерацией и текущим моментом: треки удаляют. Убрать
            // заголовок лучше, чем показать пустой ряд.
            if (SectionIsEmpty(section))
                continue;

            sections.Add(section);
        }

        return sections;
    }

    /// <summary>Общая реализация для полок из одного вида объектов (исполнители, альбомы) — читает конкретную полку по ключу и наполняет её через переданный загрузчик.</summary>
    /// <typeparam name="T">DTO объекта, в который наполняется идентификатор.</typeparam>
    /// <param name="shelfKey">Ключ конкретной полки, из которой берутся элементы.</param>
    /// <param name="kind">Ожидаемый вид элементов полки — защита от смешивания видов.</param>
    /// <param name="limit">Максимум элементов в ответе.</param>
    /// <param name="loader">Функция загрузки DTO по набору идентификаторов — <see cref="ProjectionLookups.ArtistsByIdAsync"/> или <see cref="ProjectionLookups.AlbumsByIdAsync"/>.</param>
    /// <param name="ct">Токен отмены.</param>
    private async Task<IReadOnlyList<T>> GetEntitiesAsync<T>(
        string shelfKey,
        RecommendedItemKind kind,
        int limit,
        Func<IEnumerable<Guid>, CancellationToken, Task<Dictionary<Guid, T>>> loader,
        CancellationToken ct)
    {
        var shelves = await LoadShelvesAsync(currentUser.Id, ct);

        var items = shelves
            .Where(shelf => shelf.ShelfKey == shelfKey)
            .SelectMany(shelf => shelf.Payload)
            .Where(item => item.Kind == kind)
            .Take(Math.Clamp(limit, 1, PageRequest.MaxPageSize))
            .ToList();

        if (items.Count == 0)
            return [];

        var loaded = await loader(items.Select(item => item.ItemId), ct);
        return Resolve(items, loaded);
    }

    /// <summary>Отбирает из общего списка элементов только идентификаторы нужного вида — готовит набор для одного из трёх batch-запросов в <see cref="HydrateAsync"/>.</summary>
    private static IEnumerable<Guid> Ids(
        List<(RecommendationCacheEntry Shelf, CachedRecommendation Item)> wanted, RecommendedItemKind kind) =>
        wanted.Where(w => w.Item.Kind == kind).Select(w => w.Item.ItemId).Distinct();

    /// <summary>Сохраняет закэшированный порядок, выбрасывая всё, что с тех пор удалили.</summary>
    private static List<T> Resolve<T>(List<CachedRecommendation> items, Dictionary<Guid, T> loaded) =>
        items.Where(item => loaded.ContainsKey(item.ItemId)).Select(item => loaded[item.ItemId]).ToList();

    /// <summary>Истина, если секция не содержит ни одного элемента ни в одном из трёх возможных видов — сигнал скрыть заголовок полки.</summary>
    private static bool SectionIsEmpty(RecommendationSectionDto section) =>
        (section.Tracks?.Count ?? 0) == 0
        && (section.Artists?.Count ?? 0) == 0
        && (section.Albums?.Count ?? 0) == 0;

    /// <summary>Собирает DTO трека для ответа API, добавляя причину рекомендации и, опционально, оценку.</summary>
    private static RecommendedTrackDto ToDto(TrackDto track, CachedRecommendation item, bool includeScores) =>
        new(track, ReasonOf(item), includeScores ? item.Score : null);

    /// <summary>Разворачивает закэшированный код причины и её подстановку в DTO, понятный клиенту.</summary>
    private static RecommendationReasonDto ReasonOf(CachedRecommendation item) =>
        new(item.ReasonKind, item.ReasonSubject, item.ReasonSubjectId);

}
