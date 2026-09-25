// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

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

public class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public IssuedToken CreateAccessToken(User user)
    {
        var now = clock.GetUtcNow();
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new Dictionary<string, object>
        {
            ["sub"] = user.Id.ToString(),
            ["username"] = user.Username,
            ["jti"] = Guid.CreateVersion7().ToString("N"),
        };

        if (user.IsAdmin)
            claims["role"] = "Admin";

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = claims,
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

    public SymmetricSecurityKey SigningKey => BuildSigningKey(_options);

    public static SymmetricSecurityKey BuildSigningKey(JwtOptions options)
    {
        var keyBytes = Encoding.UTF8.GetBytes(options.SigningKey);

        return new SymmetricSecurityKey(keyBytes);
    }
}

public class ClaimsPrincipalCurrentUser(ClaimsPrincipal? principal) : ICurrentUser
{
    public bool IsAuthenticated => principal?.Identity?.IsAuthenticated == true && Id != Guid.Empty;

    public Guid Id
    {
        get
        {
            var value = principal?.FindFirst("sub")?.Value
                        ?? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            return Guid.TryParse(value, out var id) ? id : Guid.Empty;
        }
    }
}
