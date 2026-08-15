namespace MusicStreaming.Application.Abstractions;

/// <param name="Username">Имя пользователя в Last.fm.</param>
/// <param name="SessionKey">Бессрочный ключ сессии — хранить только зашифрованным.</param>
public record LastfmSession(string Username, string SessionKey);

/// <param name="PlayedAt">Когда началось проигрывание; <c>null</c> — это «сейчас играет», а не прослушивание.</param>
public record LastfmTrack(
    string Artist,
    string Title,
    string? Album,
    int DurationSeconds,
    DateTimeOffset? PlayedAt);

/// <summary>
/// Отказ Last.fm.
/// </summary>
/// <param name="message">Сообщение для журнала.</param>
/// <param name="Transient">Стоит ли повторить: недоступность сервиса и превышение частоты пройдут сами, неверная подпись — нет.</param>
/// <param name="AuthFailure">Ключ сессии больше не действует — повторять бесполезно, пока пользователь не подключится заново.</param>
public class LastfmException(string message, bool Transient = false, bool AuthFailure = false)
    : Exception(message)
{
    public bool IsTransient { get; } = Transient;
    public bool IsAuthFailure { get; } = AuthFailure;
}

public interface ILastfmApi
{
    /// <summary>Заданы ли ключ и секрет приложения. Без них интеграция не предлагается вовсе.</summary>
    bool IsConfigured { get; }

    /// <summary>Адрес страницы Last.fm, где пользователь разрешает доступ.</summary>
    /// <param name="callbackUrl">Куда Last.fm вернёт браузер вместе с одноразовым токеном.</param>
    string AuthorizeUrl(string callbackUrl);

    /// <summary>Обменивает одноразовый токен на бессрочный ключ сессии.</summary>
    Task<LastfmSession> CompleteAsync(string token, CancellationToken ct = default);

    /// <summary>Отправляет «сейчас играет» или прослушивание — что именно, определяет <see cref="LastfmTrack.PlayedAt"/>.</summary>
    Task SendAsync(LastfmTrack track, string sessionKey, CancellationToken ct = default);
}

/// <summary>
/// Шифрование секретов, которые сервис хранит от имени пользователя. Отдельная абстракция, потому
/// что ключами занимается инфраструктура (у ASP.NET Core для этого есть Data Protection с уже
/// настроенным хранилищем ключей), а решает, что именно шифровать, слой приложения.
/// </summary>
public interface ISecretProtector
{
    string Protect(string value);

    /// <summary>Расшифровывает значение; <c>null</c>, если оно испорчено или зашифровано ключами, которых больше нет.</summary>
    string? Unprotect(string protectedValue);
}
