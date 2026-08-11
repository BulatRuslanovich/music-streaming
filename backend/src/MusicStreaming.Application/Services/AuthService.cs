using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;

namespace MusicStreaming.Application.Services;

public class AuthService(
    IApplicationDbContext db,
    IPasswordHasher passwordHasher,
    ITokenService tokens,
    TimeProvider clock,
    ILogger<AuthService> logger)
{
    public async Task<AuthResultDto> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var username = (request.Username ?? string.Empty).Trim().ToLowerInvariant();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);

        var hashToCheck = user?.PasswordHash ?? DummyHash;
        var passwordOk = passwordHasher.Verify(request.Password ?? string.Empty, hashToCheck);

        if (user is null || !passwordOk)
        {
            logger.LogWarning("Failed login attempt for username {Username}", username);
            throw new AuthenticationException("Invalid username or password.");
        }

        logger.LogInformation("User {UserId} signed in", user.Id);
        return await IssueAsync(user, ct);
    }

    public async Task<AuthResultDto> RefreshAsync(string? rawRefreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
            throw new AuthenticationException("Missing refresh token.");

        var hash = tokens.HashRefreshToken(rawRefreshToken);
        var now = clock.GetUtcNow();

        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored?.User is null || !stored.IsActive(now))
        {
            logger.LogWarning("Refresh rejected for token hash {Hash}", hash[..8]);
            throw new AuthenticationException("Refresh token is invalid or expired.");
        }

        stored.RevokedAt = now;

        return await IssueAsync(stored.User, ct);
    }

    public async Task LogoutAsync(string? rawRefreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
            return;

        var hash = tokens.HashRefreshToken(rawRefreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is { RevokedAt: null })
        {
            stored.RevokedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            logger.LogInformation("User {UserId} signed out", stored.UserId);
        }
    }

    public async Task<UserDto> GetUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new UserDto(u.Id, u.Username, u.DisplayName, u.IsAdmin))
            .FirstOrDefaultAsync(ct);

        return user ?? throw new NotFoundException("User not found.");
    }

    private async Task<AuthResultDto> IssueAsync(Domain.Entities.User user, CancellationToken ct)
    {
        var access = tokens.CreateAccessToken(user);
        var refresh = tokens.CreateRefreshToken(user.Id);
        db.RefreshTokens.Add(refresh.Entity);

        await PruneExpiredTokensAsync(user.Id, ct);
        await db.SaveChangesAsync(ct);

        return new AuthResultDto(
            new UserDto(user.Id, user.Username, user.DisplayName, user.IsAdmin),
            access.Value,
            access.ExpiresAt,
            refresh.RawValue,
            refresh.Entity.ExpiresAt);
    }

    private async Task PruneExpiredTokensAsync(Guid userId, CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().AddDays(-1);
        var stale = await db.RefreshTokens
            .Where(t => t.UserId == userId && (t.ExpiresAt < cutoff || (t.RevokedAt != null && t.RevokedAt < cutoff)))
            .ToListAsync(ct);

        if (stale.Count > 0)
            db.RefreshTokens.RemoveRange(stale);
    }

    private const string DummyHash = "$2a$11$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy";
}
