using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Abstractions;

/// <summary>
/// Выпуск токенов доступа и обновления.
///
/// <para>
/// Refresh-токен возвращается парой: <c>RawValue</c> уходит клиенту, а в базу ложится сущность, где
/// хранится только его хеш. Утечка дампа базы поэтому не даёт войти — по хешу токен не восстановить.
/// </para>
/// </summary>
public interface ITokenService
{
    IssuedToken CreateAccessToken(User user);
    IssuedRefreshToken CreateRefreshToken(Guid userId);
    string HashRefreshToken(string rawValue);
}

public record IssuedToken(string Value, DateTimeOffset ExpiresAt);

public record IssuedRefreshToken(string RawValue, RefreshToken Entity);
