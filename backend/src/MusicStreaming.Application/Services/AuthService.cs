// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

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
    LoginAttemptTracker attempts,
    TimeProvider clock,
    ILogger<AuthService> logger)
{
    public async Task<AuthResultDto> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var username = (request.Username ?? string.Empty).Trim().ToLowerInvariant();

        if (attempts.LockoutRemaining(username) is { } remaining)
        {
            logger.LogWarning(
                "Login for {Username} refused: the account is locked for another {Minutes:0.#} minutes",
                username, remaining.TotalMinutes);

            throw new ForbiddenException(
                "Too many failed sign-in attempts. Try again in "
                + $"{Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))} minutes.");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
        var passwordOk = passwordHasher.Verify(request.Password ?? string.Empty, user?.PasswordHash ?? "");

        if (user is null || !passwordOk)
        {
            attempts.RecordFailure(username);
            logger.LogWarning("Failed login attempt for username {Username}", username);
            throw new ForbiddenException("Invalid username or password.");
        }

        if (!user.IsActive)
        {
            logger.LogWarning("Deactivated user {UserId} tried to sign in", user.Id);
            throw new ForbiddenException("This account has been deactivated.");
        }

        attempts.RecordSuccess(username);
        logger.LogInformation("User {UserId} signed in", user.Id);
        return await IssueAsync(user, ct);
    }

    public async Task<AuthResultDto> RefreshAsync(string? rawRefreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
            throw new AuthenticationException("Missing refresh token.");

        var hash = tokens.HashRefreshToken(rawRefreshToken);
        var now = clock.GetUtcNow();

        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is { RevokedAt: { } revokedAt })
        {
            var sessionLivesOn = await db.RefreshTokens.AnyAsync(
                t => t.UserId == stored.UserId && t.RevokedAt == null && t.ExpiresAt > now, ct);

            if (!sessionLivesOn || now - revokedAt > ReuseGrace)
            {
                logger.LogWarning(
                    "Refresh token reuse detected for user {UserId}; all sessions revoked",
                    stored.UserId);

                await db.RefreshTokens
                    .Where(t => t.UserId == stored.UserId && t.RevokedAt == null)
                    .ExecuteUpdateAsync(t => t.SetProperty(token => token.RevokedAt, now), ct);

                throw new AuthenticationException("Refresh token is invalid or expired.");
            }

            logger.LogDebug(
                "Refresh token of user {UserId} was rotated moments ago; treating as a concurrent refresh",
                stored.UserId);
        }

        if (stored?.User is null || stored.ExpiresAt <= now || !stored.User.IsActive)
        {
            logger.LogWarning("Refresh rejected for token hash {Hash}", hash[..8]);
            throw new AuthenticationException("Refresh token is invalid or expired.");
        }

        stored.RevokedAt = now;

        return await IssueAsync(stored.User, ct);
    }

    public async Task LogoutAsync(string? rawRefreshToken, CancellationToken ct)
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

    public async Task<AuthResultDto> ChangePasswordAsync(
        ChangePasswordRequest request, Guid userId, CancellationToken ct)
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

    public async Task<UserDto> GetUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users
            .Where(u => u.Id == userId)
            .Select(ToDto.UserProjection)
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
            ToDto.User(user),
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

    private static readonly TimeSpan ReuseGrace = TimeSpan.FromSeconds(20);
}
