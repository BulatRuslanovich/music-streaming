using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Abstractions;

public interface ITokenService
{
    IssuedToken CreateAccessToken(User user);
    IssuedRefreshToken CreateRefreshToken(Guid userId);
    string HashRefreshToken(string rawValue);
}

public record IssuedToken(string Value, DateTimeOffset ExpiresAt);

public record IssuedRefreshToken(string RawValue, RefreshToken Entity);
