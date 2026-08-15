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
            throw new ForbiddenException("Invalid username or password.");
        }

        // Проверяется после пароля намеренно: иначе ответ рассказывал бы, что такая учётная запись
        // существует, любому, кто угадал имя.
        if (!user.IsActive)
        {
            logger.LogWarning("Deactivated user {UserId} tried to sign in", user.Id);
            throw new ForbiddenException("This account has been deactivated.");
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

        // Деактивация отзывает выданные токены, но проверка здесь всё равно нужна: обновление —
        // единственная точка, где сессия продлевается, и закрыть её значит закрыть доступ навсегда,
        // как бы токен ни оказался на руках.
        if (stored?.User is null || !stored.IsActive(now) || !stored.User.IsActive)
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

    /// <summary>
    /// Смена собственного пароля. Все прежние сессии отзываются, а текущая тут же получает новую
    /// пару токенов: человек, меняющий пароль, хочет закрыть чужие устройства, а не своё.
    /// </summary>
    public async Task<AuthResultDto> ChangePasswordAsync(
        ChangePasswordRequest request, Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("User not found.");

        if (!passwordHasher.Verify(request.CurrentPassword ?? string.Empty, user.PasswordHash))
            throw new ForbiddenException("The current password is not correct.");

        var password = PasswordPolicy.Validate(request.NewPassword);
        if (passwordHasher.Verify(password, user.PasswordHash))
            throw new ValidationException("The new password must differ from the current one.");

        user.PasswordHash = passwordHasher.Hash(password);

        var now = clock.GetUtcNow();
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(t => t.SetProperty(token => token.RevokedAt, now), ct);

        logger.LogInformation("User {UserId} changed their password", userId);
        return await IssueAsync(user, ct);
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
