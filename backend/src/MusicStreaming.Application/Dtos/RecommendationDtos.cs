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
