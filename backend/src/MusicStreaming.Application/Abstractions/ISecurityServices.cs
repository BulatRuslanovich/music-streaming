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

    IssuedToken CreateAccessToken(User user);


    IssuedRefreshToken CreateRefreshToken(Guid userId);

    string HashRefreshToken(string rawValue);
}

public sealed record IssuedToken(string Value, DateTimeOffset ExpiresAt);

public sealed record IssuedRefreshToken(string RawValue, RefreshToken Entity);


public interface ICurrentUser
{
    Guid Id { get; }
    bool IsAuthenticated { get; }
}


public static class AppClaims
{
    public const string UserId = "sub";
    public const string Username = "username";

    public const string Role = "role";
}

public static class AppRoles
{
    public const string Admin = "Admin";
}


public static class AppPolicies
{
    public const string Admin = "Admin";
}
