namespace MusicStreaming.Domain.Entities.Integrations;

/// <summary>Какая внешняя операция стоит за заданием. Значения хранятся в базе — только дописывать.</summary>
public enum OutboundJobKind
{
    /// <summary>«Сейчас играет» в Last.fm. Живёт минуты, поэтому не переспрашивается.</summary>
    LastfmNowPlaying = 1,

    /// <summary>Прослушивание в Last.fm. Принимается задним числом, поэтому переспрашивается долго.</summary>
    LastfmScrobble = 2,
}

public enum OutboundJobState
{
    Pending = 0,
    Succeeded = 1,

    /// <summary>Попытки исчерпаны или ответ был окончательным отказом; задание больше не берётся.</summary>
    Failed = 2,
}

/// <summary>
/// Одно обращение к внешнему сервису, которое переживает перезапуск.
///
/// <para>
/// Очередь в памяти здесь не годится: у прослушивания есть точное время, ради которого его и
/// отправляют, а падение сервиса или получасовая недоступность Last.fm не должны стоить
/// пользователю его истории. Строка в базе даёт и повтор с выдержкой, и естественную защиту от
/// дублей через <see cref="DedupeKey"/>.
/// </para>
///
/// <para>
/// Таблица намеренно не знает ничего про Last.fm: <see cref="Payload"/> — это непрозрачный JSON,
/// который разбирает только обработчик своего вида. Следующая исходящая интеграция добавит вид и
/// обработчик, а не вторую таблицу с тем же повтором и той же выдержкой.
/// </para>
/// </summary>
public class OutboundJob
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public OutboundJobKind Kind { get; set; }

    /// <summary>От чьего имени выполняется обращение — по нему обработчик находит учётные данные.</summary>
    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Данные операции в виде JSON; формат определяет <see cref="Kind"/>.</summary>
    public string Payload { get; set; } = "{}";

    /// <summary>
    /// Естественный ключ операции. Уникален, поэтому повторная постановка того же прослушивания
    /// (второй heartbeat того же проигрывания, повторный проход воркера) отсекается базой, а не
    /// проверкой в коде, которая гонялась бы сама с собой.
    /// </summary>
    public string DedupeKey { get; set; } = string.Empty;

    public int Attempts { get; set; }

    /// <summary>Момент, раньше которого задание не берут; после каждой неудачи отодвигается с экспоненциальной выдержкой.</summary>
    public DateTimeOffset NextAttemptAt { get; set; }

    public OutboundJobState State { get; set; }

    /// <summary>Последняя ошибка (обрезанная), если попытка не удалась.</summary>
    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
