using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

public sealed partial class AdminUserService(
    IApplicationDbContext db,
    IPasswordHasher passwordHasher,
    ILogger<AdminUserService> logger)
{
    public const int MinPasswordLength = 8;

    public const int MaxPasswordLength = 72;

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{2,99}$")]
    private static partial Regex UsernamePattern { get; }

    public async Task<PagedResult<AdminUserDto>> GetUsersAsync(
        PageRequest page, CancellationToken ct = default)
    {
        var query = db.Users.AsNoTracking();
        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(u => u.Username)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(u => new AdminUserDto(u.Id, u.Username, u.DisplayName, u.IsAdmin, u.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<AdminUserDto>(items, total, page.Page, page.PageSize);
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
            // Lost the race against a concurrent create on the unique username index.
            throw new ConflictException("A user with that username already exists.");
        }

        logger.LogInformation("Created user {Username} (admin: {IsAdmin})", username, user.IsAdmin);
        return new AdminUserDto(user.Id, user.Username, user.DisplayName, user.IsAdmin, user.CreatedAt);
    }
}
