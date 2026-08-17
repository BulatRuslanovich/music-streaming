namespace MusicStreaming.Domain.Entities;

/// <summary>
/// Выданный токен обновления — то, что на самом деле удерживает сессию.
///
/// <para>
/// Хранится <see cref="TokenHash"/>, а не сам токен: утечка дампа базы не должна давать возможность
/// войти. Отозванная строка не удаляется сразу и живёт ещё сутки — именно по ней обнаруживается
/// повторное использование украденного токена, а без неё предъявление краденого выглядело бы просто
/// как неизвестный токен.
/// </para>
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
