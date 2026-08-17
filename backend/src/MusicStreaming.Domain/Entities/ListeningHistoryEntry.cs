namespace MusicStreaming.Domain.Entities;

/// <summary>
/// Запись в истории прослушиваний — источник полки «недавно слушали».
///
/// <para>
/// Обновляется, а не дописывается: пауза посреди песни и продолжение через десять минут должны
/// остаться одним прослушиванием. Список к тому же подрезается до последней тысячи на пользователя.
/// Для «недавнего» так и правильно, но повторы и скипы при этом теряются — движку рекомендаций они
/// нужны, и он читает собственный журнал <see cref="Recommendations.PlaybackEvent"/>, а годовая
/// статистика — третью, почасовую сводку <see cref="ListeningStat"/>.
/// </para>
/// </summary>
public class ListeningHistoryEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }
    public DateTimeOffset PlayedAt { get; set; } = DateTimeOffset.UtcNow;
    public int PlaybackPosition { get; set; }
}
