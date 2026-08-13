namespace MusicStreaming.Domain.Entities.Recommendations;

/// <summary>
/// Один слот предрассчитанной полки. Хранятся только идентификаторы: названия, наличие обложки и
/// признак избранного подтягиваются при чтении через существующие проекции, поэтому переименование
/// или новый лайк видны сразу, а не заморожены в кэше.
/// </summary>
/// <param name="ItemId">Идентификатор объекта — трека, исполнителя или альбома, в зависимости от <paramref name="Kind"/>.</param>
/// <param name="Kind">Вид объекта, на который ссылается слот.</param>
/// <param name="Score">Оценка, с которой объект попал на полку — используется для клиентской сортировки и отладки.</param>
/// <param name="ReasonKind">Код причины рекомендации.</param>
/// <param name="ReasonSubject">Имя объекта для подстановки в формулировку причины.</param>
/// <param name="ReasonSubjectId">Идентификатор объекта из <paramref name="ReasonSubject"/>.</param>
public record CachedRecommendation(
    Guid ItemId,
    RecommendedItemKind Kind,
    double Score,
    string ReasonKind,
    string? ReasonSubject,
    Guid? ReasonSubjectId);

/// <summary>
/// Сгенерированная полка, готовая к выдаче. Чтение — это одна выборка по первичному ключу плюс
/// один запрос на гидрацию, что и удерживает эндпоинты рекомендаций в бюджете по задержке.
/// </summary>
public class RecommendationCacheEntry
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>
    /// Идентифицирует полку вместе с её затравкой — <c>forYou</c>, <c>similarTo:{trackId}</c>,
    /// <c>becauseYouListened:{artistId}</c>.
    /// </summary>
    public string ShelfKey { get; set; } = string.Empty;

    /// <summary>
    /// Место полки на персональной главной. Хранится, а не вычисляется, чтобы чтение было одним
    /// упорядоченным проходом по строкам пользователя, без логики раскладки на горячем пути.
    /// </summary>
    public int Position { get; set; }

    /// <summary>Элементы полки в порядке показа.</summary>
    public IReadOnlyList<CachedRecommendation> Payload { get; set; } = [];

    public DateTimeOffset GeneratedAt { get; set; }

    /// <summary>Момент, после которого полка считается устаревшей; чтение всё равно отдаёт её как есть, но ставит пользователя в очередь на пересчёт.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Проход генерации, которым построена эта полка — связывает её с записью <see cref="RecommendationRun"/> для отладки.</summary>
    public Guid RunId { get; set; }
}

/// <summary>
/// Трек, который был показан пользователю. Пишется при генерации полки, а не при её выдаче, чтобы
/// путь чтения ничего не записывал; <see cref="ClickedAt"/> проставляется позже роллапом, когда
/// приходит прослушивание с источником «рекомендация».
/// </summary>
public class RecommendationImpression
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid TrackId { get; set; }
    public Track? Track { get; set; }

    /// <summary>Полка, на которой показан трек.</summary>
    public string ShelfKey { get; set; } = string.Empty;

    /// <summary>Позиция трека внутри полки в момент показа.</summary>
    public int Position { get; set; }

    public DateTimeOffset ShownAt { get; set; }

    /// <summary>Момент, когда показ привёл к прослушиванию; <c>null</c>, пока клика не было — заполняется ролл-апом при получении соответствующего события.</summary>
    public DateTimeOffset? ClickedAt { get; set; }
}

/// <summary>
/// Журнал проходов генерации: что запускалось, сколько заняло, с каким объёмом данных работало.
/// Читается диагностическим эндпоинтом для администратора и помогает, когда полка выглядит неверно.
/// </summary>
public class RecommendationRun
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Null для проходов по всей библиотеке, например пересчёта похожести.</summary>
    public Guid? UserId { get; set; }

    public RecommendationTrigger Trigger { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public int DurationMs { get; set; }

    /// <summary>Сколько кандидатов было рассмотрено за проход.</summary>
    public int CandidateCount { get; set; }

    /// <summary>Сколько полок было построено за проход.</summary>
    public int ShelfCount { get; set; }

    public RecommendationRunStatus Status { get; set; }

    /// <summary>Сообщение исключения (обрезанное до 2000 символов), если <see cref="Status"/> — <see cref="RecommendationRunStatus.Failed"/>.</summary>
    public string? Error { get; set; }
}
