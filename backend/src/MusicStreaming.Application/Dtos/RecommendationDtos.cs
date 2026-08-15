namespace MusicStreaming.Application.Dtos;

/// <summary>
/// Почему что-то рекомендовано — данными, а не предложением.
///
/// <para>
/// Сервер не присылает «Потому что вы слушали Radiohead». Интерфейс поставляется больше чем на
/// одном языке с типизированным словарём, поэтому формулировка принадлежит клиенту: он получает
/// вид причины и предмет и подставляет ту фразу, которой требует его локаль.
/// </para>
/// </summary>
public record RecommendationReasonDto(string Kind, string? Subject, Guid? SubjectId);

/// <summary>
/// Рекомендованный трек. <c>Score</c> заполняется только для администраторов, явно его
/// запросивших: число релевантности ничего не значит для слушателя и ему не показывается.
/// </summary>
public record RecommendedTrackDto(TrackDto Track, RecommendationReasonDto Reason, double? Score);

/// <summary>
/// Одна полка. Ровно одна из трёх коллекций заполнена — в зависимости от того, из чего полка состоит.
/// </summary>
public record RecommendationSectionDto(
    string Key,
    string BaseKey,
    RecommendationReasonDto? Reason,
    IReadOnlyList<RecommendedTrackDto>? Tracks,
    IReadOnlyList<ArtistDto>? Artists,
    IReadOnlyList<AlbumDto>? Albums);

/// <summary>Персональная главная страница.</summary>
public record RecommendationHomeDto(
    IReadOnlyList<RecommendationSectionDto> Sections,
    bool IsColdStart,
    DateTimeOffset? GeneratedAt);

/// <summary>
/// Запрос очередной пачки радио. Очередь хранится у клиента, поэтому он же сообщает, чего
/// предлагать не надо.
/// </summary>
/// <param name="SeedTrackId">Последний реально сыгравший трек; <c>null</c> — сервер возьмёт последний понравившийся из истории.</param>
/// <param name="Exclude">Треки, уже стоящие в очереди.</param>
/// <param name="Limit">Сколько треков вернуть; по умолчанию <see cref="Services.Recommendations.RadioService.BatchSize"/>.</param>
public record RadioRequest(Guid? SeedTrackId, IReadOnlyList<Guid>? Exclude, int? Limit);

/// <summary>Пачка продолжения. Пустой список — продолжать нечем; это не ошибка.</summary>
public record RadioBatchDto(IReadOnlyList<RecommendedTrackDto> Tracks, Guid? SeedTrackId)
{
    public static readonly RadioBatchDto Empty = new([], null);
}
