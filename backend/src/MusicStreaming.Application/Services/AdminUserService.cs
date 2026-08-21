// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

public partial class AdminUserService(
    IApplicationDbContext db,
    IPasswordHasher passwordHasher,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<AdminUserService> logger)
{
    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{4,19}$")]
    private static partial Regex UsernamePattern { get; }

    public async Task<PagedResult<UserDto>> GetUsersAsync(PageRequest page, CancellationToken ct = default)
    {
        return await db.Users.AsNoTracking()
            .OrderBy(u => u.Username)
            .ToPagedAsync(page, u => new UserDto(u.Id, u.Username, u.DisplayName, u.IsAdmin, u.IsActive, u.CreatedAt), ct);
    }

    public async Task<UserDto> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var username = (request.Username ?? string.Empty).Trim().ToLowerInvariant();

        if (!UsernamePattern.IsMatch(username))
        {
            throw new ValidationException(
                "A username must be 5-20 characters of lower-case letters, digits, dot, dash or underscore.");
        }

        var password = PasswordPolicy.Validate(request.Password);

        var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? username
            : request.DisplayName.Trim();

        if (displayName.Length > 100)
            throw new ValidationException("The display name is longer than 100 characters.");

        var user = new User
        {
            Username = username,
            DisplayName = displayName,
            PasswordHash = passwordHasher.Hash(password),
            IsAdmin = request.IsAdmin,
        };

        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException("A user with that username already exists.");
        }

        logger.LogInformation("Created user {Username} (admin: {IsAdmin})", username, user.IsAdmin);
        return ToDto(user);
    }

    public async Task<UserDto> SetActiveAsync(Guid userId, bool active, CancellationToken ct = default)
    {
        var user = await FindAsync(userId, ct);

        if (!active)
        {
            if (userId == currentUser.Id)
                throw new ValidationException("You cannot deactivate your own account, dodik!");

            await RefuseIfLastAdminAsync(user, ct);
        }

        user.IsActive = active;

        if (!active)
            await RevokeTokensAsync(userId, ct);

        await db.SaveChangesAsync(ct);

        logger.LogInformation("User {UserId} was {State}", userId, active ? "reactivated" : "deactivated");
        return ToDto(user);
    }

    public async Task<UserDto> SetAdminAsync(Guid userId, bool isAdmin, CancellationToken ct = default)
    {
        var user = await FindAsync(userId, ct);

        if (!isAdmin)
        {
            if (userId == currentUser.Id)
                throw new ValidationException("You cannot revoke your own administrator rights, dodik!");

            await RefuseIfLastAdminAsync(user, ct);
        }

        user.IsAdmin = isAdmin;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "User {UserId} is {State} an administrator", userId, isAdmin ? "now" : "no longer");

        return ToDto(user);
    }

    public async Task ResetPasswordAsync(Guid userId, string? newPassword, CancellationToken ct = default)
    {
        var user = await FindAsync(userId, ct);

        user.PasswordHash = passwordHasher.Hash(PasswordPolicy.Validate(newPassword));
        await RevokeTokensAsync(userId, ct);
        await db.SaveChangesAsync(ct);

        logger.LogWarning("Password of user {UserId} was reset by an administrator", userId);
    }

    public async Task RevokeSessionsAsync(Guid userId, CancellationToken ct = default)
    {
        await FindAsync(userId, ct);
        await RevokeTokensAsync(userId, ct);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("All sessions of user {UserId} were revoked", userId);
    }

    private async Task<User> FindAsync(Guid userId, CancellationToken ct) =>
        await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
        ?? throw new NotFoundException("User not found.");

    // INFO: такой исход маловероятен, но раз в год и у деда че то там стреляет 
    private async Task RefuseIfLastAdminAsync(User user, CancellationToken ct)
    {
        if (!user.IsAdmin || !user.IsActive)
            return;

        var others = await db.Users.CountAsync(
            u => u.IsAdmin && u.IsActive && u.Id != user.Id, ct);

        if (others == 0)
            throw new ValidationException("This is the last active administrator, bro");
    }

    private async Task RevokeTokensAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(t => t.SetProperty(token => token.RevokedAt, now), ct);
    }

    private static UserDto ToDto(User user) =>
        new(user.Id, user.Username, user.DisplayName, user.IsAdmin, user.IsActive, user.CreatedAt);
}
