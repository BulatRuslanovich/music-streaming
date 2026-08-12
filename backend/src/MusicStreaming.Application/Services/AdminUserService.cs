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
    ILogger<AdminUserService> logger)
{
    private const int MinPasswordLength = 8;
    private const int MaxPasswordLength = 72;

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{2,99}$")]
    private static partial Regex UsernamePattern { get; }

    public async Task<PagedResult<AdminUserDto>> GetUsersAsync(
        PageRequest page, CancellationToken ct = default)
    {
        return await db.Users.AsNoTracking()
            .OrderBy(u => u.Username)
            .ToPagedAsync(
                page,
                u => new AdminUserDto(u.Id, u.Username, u.DisplayName, u.IsAdmin, u.CreatedAt),
                ct);
    }

    public async Task<AdminUserDto> CreateUserAsync(
        CreateUserRequest request, CancellationToken ct = default)
    {
        var username = (request.Username ?? string.Empty).Trim().ToLowerInvariant();

        if (!UsernamePattern.IsMatch(username))
        {
            throw new ValidationException(
                "A username must be 3-100 characters of lower-case letters, digits, dot, dash or underscore.");
        }

        var password = request.Password ?? string.Empty;
        if (password.Length is < MinPasswordLength or > MaxPasswordLength)
        {
            throw new ValidationException(
                $"The password must be {MinPasswordLength}-{MaxPasswordLength} characters long.");
        }

        var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? username
            : request.DisplayName.Trim();

        if (displayName.Length > 100)
            throw new ValidationException("The display name is longer than 100 characters.");

        if (await db.Users.AnyAsync(u => u.Username == username, ct))
            throw new ConflictException("A user with that username already exists.");

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
        return new AdminUserDto(user.Id, user.Username, user.DisplayName, user.IsAdmin, user.CreatedAt);
    }
}
