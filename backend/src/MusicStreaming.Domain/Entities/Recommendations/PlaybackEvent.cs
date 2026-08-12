namespace MusicStreaming.Domain.Entities.Recommendations;

/// <summary>
/// Один сырой поведенческий сигнал, только на добавление.
///
/// Намеренно отделён от <see cref="ListeningHistoryEntry"/>: история перезаписывает свою строку
/// внутри 30-минутного окна и подрезается до последних N записей на пользователя. Для полки
/// «недавно прослушанное» это правильно, но уничтожает ровно те данные, которые нужны
/// рекомендациям, — повторы, скипы и процент дослушивания. События же записываются один раз,
/// никогда не обновляются и сворачиваются в долговечные таблицы аффинити ещё до того, как их
/// подрежет ретеншн.
/// </summary>
public class PlaybackEvent
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>
    /// Порядок поступления, назначается базой.
    ///
    /// <para>
    /// Роллап возобновляется с watermark, и <see cref="OccurredAt"/> для этого не годится: это
    /// часы клиента, поэтому батч из заснувшей вкладки приходит с временем более старым, чем уже
    /// обработанные события, и был бы пропущен. Серверный счётчик монотонен по построению. Пишет
    /// события единственный воркер, так что дыр в видимости не возникает.
    /// </para>
    /// </summary>
    public long Sequence { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Null для событий уровня сущности (открыт исполнитель, альбом, плейлист).</summary>
    public Guid? TrackId { get; set; }
    public Track? Track { get; set; }

    /// <summary>
    /// Исполнитель, альбом или плейлист, к которому относится событие уровня сущности. Не внешний
    /// ключ: три возможные цели лежат в разных таблицах, а устаревший идентификатор здесь безвреден.
    /// </summary>
    public Guid? EntityId { get; set; }

    public PlaybackEventType Type { get; set; }

    /// <summary>Часы клиента; при приёме зажимаются, чтобы сбитое время устройства не портило затухание.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Позиция воспроизведения в момент события.</summary>
    public int PositionSeconds { get; set; }

    /// <summary>Реально прослушанные секунды — перемотка и пауза не засчитываются.</summary>
    public int ListenedSeconds { get; set; }

    /// <summary>Длительность трека, какой её знал клиент; хранится, чтобы правка тегов не ломала процент.</summary>
    public int DurationSeconds { get; set; }

    /// <summary>Объединяет события в одну сессию прослушивания — единицу совстречаемости.</summary>
    public Guid SessionId { get; set; }

    public PlaybackSource Source { get; set; }

    /// <summary>Альбом, исполнитель, плейлист или трек-затравка, с которого начали воспроизведение.</summary>
    public Guid? SourceId { get; set; }

    public string Platform { get; set; } = "web";
}
