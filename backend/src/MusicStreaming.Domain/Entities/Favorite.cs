namespace MusicStreaming.Domain.Entities;

/// <summary>
/// Отметка «нравится». Собственного идентификатора нет намеренно: сущность и есть пара
/// «пользователь и трек», она же первичный ключ, — и повторно лайкнуть один трек попросту нельзя.
/// </summary>
public class Favorite
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
