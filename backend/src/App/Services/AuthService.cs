// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Infrastructure.Persistence;
using Infrastructure.Security;
using App.Common;
using App.Dtos;
using Microsoft.EntityFrameworkCore;
using Domain.Common;
using Domain.Entities;

namespace App.Services;

public class AuthService(
    ApplicationDbContext db,
    BCryptPasswordHasher passwordHasher,
    JwtTokenService tokens,
    TimeProvider clock,
    ILogger<AuthService> logger)
{
    public async Task<AuthResultDto> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var username = Normalize.Username(request.Username);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
        var passwordOk = passwordHasher.Verify(request.Password, user?.PasswordHash ?? "");

        if (user is null || !passwordOk)
        {
            logger.LogWarning("Failed login attempt for username {Username}", username);
            throw new ForbiddenException("Invalid username or password.");
        }

        if (!user.IsActive)
        {
            logger.LogWarning("Deactivated user {UserId} tried to sign in", user.Id);
            throw new ForbiddenException("This account has been deactivated.");
        }

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
            var sessionLivesOn = await db.RefreshTokens
                .AnyAsync(t => t.UserId == stored.UserId && t.RevokedAt == null && t.ExpiresAt > now, ct);

            if (!sessionLivesOn || now - revokedAt > ReuseGrace)
            {
                logger.LogWarning(
                    "Refresh token reuse detected for user {UserId}; all sessions revoked",
                    stored.UserId);

                await db.RefreshTokens.RevokeAllAsync(stored.UserId, ct);

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
        if (stored is not null)
        {
            db.RefreshTokens.Remove(stored);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("User {UserId} signed out", stored.UserId);
        }
    }

    public async Task<AuthResultDto> ChangePasswordAsync(
        ChangePasswordRequest request, Guid userId, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("User not found.");

        if (!passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new ForbiddenException("The current password is not correct.");

        user.PasswordHash = passwordHasher.Hash(PasswordPolicy.Validate(request.NewPassword));

        await db.RefreshTokens.RevokeAllAsync(userId, ct);

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

    private async Task<AuthResultDto> IssueAsync(User user, CancellationToken ct)
    {
        var refresh = tokens.CreateRefreshToken(user.Id);
        var access = tokens.CreateAccessToken(user, refresh.Entity.Id);
        db.RefreshTokens.Add(refresh.Entity);

        var cutoff = clock.GetUtcNow().AddDays(-1);
        var stale = await db.RefreshTokens
            .Where(t => t.UserId == user.Id && (t.ExpiresAt < cutoff || (t.RevokedAt != null && t.RevokedAt < cutoff)))
            .ToListAsync(ct);

        db.RefreshTokens.RemoveRange(stale);
        await db.SaveChangesAsync(ct);

        return new AuthResultDto(
            ToDto.User(user),
            access.Value,
            access.ExpiresAt,
            refresh.RawValue,
            refresh.Entity.ExpiresAt);
    }

    private static readonly TimeSpan ReuseGrace = TimeSpan.FromSeconds(20);
}
