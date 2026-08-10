using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Infrastructure.Security;

/// <summary>
/// Issues short-lived HMAC-signed JWT access tokens and opaque, single-use refresh tokens.
///
/// Refresh tokens are random 256-bit values; only their SHA-256 digest is persisted, so a leaked
/// database still does not yield a usable token. They are plain hashes rather than BCrypt because
/// the value is already full-entropy random and lookup must be a single indexed query.
/// </summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public IssuedToken CreateAccessToken(User user)
    {
        var now = clock.GetUtcNow();
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                [AppClaims.Username] = user.Username,
                [JwtRegisteredClaimNames.Jti] = Guid.CreateVersion7().ToString("N"),
            },
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return new IssuedToken(token, expiresAt);
    }

    public IssuedRefreshToken CreateRefreshToken(Guid userId)
    {
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var now = clock.GetUtcNow();

        var entity = new RefreshToken
        {
            UserId = userId,
            TokenHash = HashRefreshToken(raw),
            CreatedAt = now,
            ExpiresAt = now.AddDays(_options.RefreshTokenDays),
        };

        return new IssuedRefreshToken(raw, entity);
    }

    public string HashRefreshToken(string rawValue) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawValue))).ToLowerInvariant();

    /// <summary>The symmetric key used for both signing and validation.</summary>
    public SymmetricSecurityKey SigningKey => BuildSigningKey(_options);

    public static SymmetricSecurityKey BuildSigningKey(JwtOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SigningKey))
            throw new InvalidOperationException("Jwt:SigningKey is not configured.");

        var keyBytes = Encoding.UTF8.GetBytes(options.SigningKey);
        if (keyBytes.Length < 32)
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes (256 bits) long.");

        return new SymmetricSecurityKey(keyBytes);
    }
}

/// <summary>Claim names as they appear in the token payload.</summary>
internal static class JwtRegisteredClaimNames
{
    public const string Sub = "sub";
    public const string Jti = "jti";
}

/// <summary>Reads the caller's id from the claims of the current request.</summary>
public sealed class ClaimsPrincipalCurrentUser(ClaimsPrincipal? principal) : ICurrentUser
{
    public bool IsAuthenticated => principal?.Identity?.IsAuthenticated == true && Id != Guid.Empty;

    public Guid Id
    {
        get
        {
            var value = principal?.FindFirst(AppClaims.UserId)?.Value
                        ?? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            return Guid.TryParse(value, out var id) ? id : Guid.Empty;
        }
    }
}
