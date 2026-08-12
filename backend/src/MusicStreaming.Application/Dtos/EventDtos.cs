namespace MusicStreaming.Application.Dtos;

/// <summary>
/// Пачка поведенческих сигналов, отправленная клиентом.
///
/// Клиент батчит, а не шлёт по одному событию: пролистывание очереди за несколько секунд даёт
/// десяток сигналов, и запрос на каждый скип обошёлся бы дороже, чем стоят сами данные.
/// </summary>
public record RecordEventsRequest(IReadOnlyList<PlaybackEventRequest>? Events);

/// <summary>
/// Один сообщённый сигнал.
///
/// <para>
/// <c>Type</c> и <c>Source</c> приходят строками, а не перечислениями, намеренно: клиент из более
/// новой или более старой сборки может прислать имя, которого этот сервер не знает, и неизвестное
/// имя должно стоить одного события, а не отклонения всего батча с ошибкой десериализации.
/// </para>
///
/// <para>Всё остальное необязательно; отсутствующие значения подставляются и зажимаются сервером.</para>
/// </summary>
public record PlaybackEventRequest(
    string? Type,
    Guid? TrackId,
    Guid? EntityId,
    DateTimeOffset? OccurredAt,
    int? PositionSeconds,
    int? ListenedSeconds,
    int? DurationSeconds,
    Guid? SessionId,
    string? Source,
    Guid? SourceId,
    string? Platform);
