using System.Security.Claims;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public interface ITokenService
{
    /// <summary>Issues a signed JWT access token for the user.</summary>
    IssuedToken CreateAccessToken(User user);

    /// <summary>
    /// Creates an opaque refresh token: the caller stores <see cref="IssuedRefreshToken.Entity"/>
    /// and hands <see cref="IssuedRefreshToken.RawValue"/> to the client.
    /// </summary>
    IssuedRefreshToken CreateRefreshToken(Guid userId);

    string HashRefreshToken(string rawValue);
}

public sealed record IssuedToken(string Value, DateTimeOffset ExpiresAt);

public sealed record IssuedRefreshToken(string RawValue, RefreshToken Entity);

/// <summary>Identity of the caller behind the current request.</summary>
public interface ICurrentUser
{
    Guid Id { get; }
    bool IsAuthenticated { get; }
}

/// <summary>Marker used by the API layer to build a <see cref="ClaimsPrincipal"/> subject claim.</summary>
public static class AppClaims
{
    public const string UserId = "sub";
    public const string Username = "username";
}
