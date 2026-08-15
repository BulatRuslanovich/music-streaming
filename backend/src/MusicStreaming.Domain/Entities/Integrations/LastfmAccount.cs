namespace MusicStreaming.Domain.Entities.Integrations;

/// <summary>
/// Связь учётной записи Caimack с учётной записью Last.fm.
///
/// <para>
/// <see cref="SessionKey"/> бессрочен и позволяет писать в чужой профиль, поэтому хранится
/// зашифрованным (см. <c>ISecretProtector</c>): дамп базы сам по себе не должен отдавать доступ к
/// стороннему сервису.
/// </para>
/// </summary>
public class LastfmAccount
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Имя пользователя в Last.fm — показывается в интерфейсе, чтобы было видно, куда именно уходят прослушивания.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Зашифрованный ключ сессии Last.fm.</summary>
    public string SessionKey { get; set; } = string.Empty;

    /// <summary>Выключено — связь сохранена, но ничего не отправляется.</summary>
    public bool Enabled { get; set; } = true;

    public DateTimeOffset ConnectedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Когда последнее прослушивание действительно доехало — единственный честный признак живой интеграции.</summary>
    public DateTimeOffset? LastScrobbleAt { get; set; }
}
