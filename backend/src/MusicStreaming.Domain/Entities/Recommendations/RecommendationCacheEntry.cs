namespace MusicStreaming.Domain.Entities.Recommendations;

/// <summary>
/// Один слот предрассчитанной полки. Хранятся только идентификаторы: названия, наличие обложки и
/// признак избранного подтягиваются при чтении через существующие проекции, поэтому переименование
/// или новый лайк видны сразу, а не заморожены в кэше.
/// </summary>
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

    public IReadOnlyList<CachedRecommendation> Payload { get; set; } = [];

    public DateTimeOffset GeneratedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
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

    public string ShelfKey { get; set; } = string.Empty;
    public int Position { get; set; }

    public DateTimeOffset ShownAt { get; set; }
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
    public int CandidateCount { get; set; }
    public int ShelfCount { get; set; }
    public RecommendationRunStatus Status { get; set; }
    public string? Error { get; set; }
}
