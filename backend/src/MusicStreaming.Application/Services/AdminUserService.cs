using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

/// <summary>
/// Управление учётными записями.
///
/// <para>
/// Удаления здесь нет намеренно: идентификатор пользователя стоит в плейлистах, избранном, истории,
/// событиях, аффинити и показах, поэтому удаление уносит с собой всё, что человек когда-либо
/// слушал, и отменить это нечем. Деактивация закрывает вход, отзывает сессии и полностью
/// обратима — а это ровно то, ради чего удаление обычно и зовут.
/// </para>
/// </summary>
public partial class AdminUserService(
    IApplicationDbContext db,
    IPasswordHasher passwordHasher,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<AdminUserService> logger)
{
    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{2,99}$")]
    private static partial Regex UsernamePattern { get; }

    public async Task<PagedResult<AdminUserDto>> GetUsersAsync(
        PageRequest page, CancellationToken ct = default)
    {
        return await db.Users.AsNoTracking()
            .OrderBy(u => u.Username)
            .ToPagedAsync(
                page,
                u => new AdminUserDto(u.Id, u.Username, u.DisplayName, u.IsAdmin, u.IsActive, u.CreatedAt),
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

        var password = PasswordPolicy.Validate(request.Password);

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
        return Describe(user);
    }

    /// <summary>Включает или выключает учётную запись; выключение заодно обрывает все её сессии.</summary>
    public async Task<AdminUserDto> SetActiveAsync(Guid userId, bool active, CancellationToken ct = default)
    {
        var user = await FindAsync(userId, ct);

        if (!active)
        {
            Refuse(userId, "You cannot deactivate your own account.");
            await RefuseIfLastAdminAsync(user, ct);
        }

        user.IsActive = active;

        if (!active)
            await RevokeTokensAsync(userId, ct);

        await db.SaveChangesAsync(ct);

        logger.LogInformation("User {UserId} was {State}", userId, active ? "reactivated" : "deactivated");
        return Describe(user);
    }

    /// <summary>Выдаёт или снимает права администратора.</summary>
    public async Task<AdminUserDto> SetAdminAsync(Guid userId, bool isAdmin, CancellationToken ct = default)
    {
        var user = await FindAsync(userId, ct);

        if (!isAdmin)
        {
            // Снять права с себя — самый быстрый способ остаться без доступа к админке вообще.
            Refuse(userId, "You cannot revoke your own administrator rights.");
            await RefuseIfLastAdminAsync(user, ct);
        }

        user.IsAdmin = isAdmin;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "User {UserId} is {State} an administrator", userId, isAdmin ? "now" : "no longer");

        return Describe(user);
    }

    /// <summary>Задаёт новый пароль. Все сессии отзываются: смена пароля должна закрывать доступ везде, где им уже пользовались.</summary>
    public async Task ResetPasswordAsync(Guid userId, string? newPassword, CancellationToken ct = default)
    {
        var user = await FindAsync(userId, ct);

        user.PasswordHash = passwordHasher.Hash(PasswordPolicy.Validate(newPassword));
        await RevokeTokensAsync(userId, ct);
        await db.SaveChangesAsync(ct);

        logger.LogWarning("Password of user {UserId} was reset by an administrator", userId);
    }

    /// <summary>Обрывает все сессии учётной записи, не трогая ни пароль, ни доступ как таковой.</summary>
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

    private void Refuse(Guid userId, string message)
    {
        if (userId == currentUser.Id)
            throw new ValidationException(message);
    }

    /// <summary>
    /// Последний действующий администратор неприкосновенен: без него некому вернуть права никому,
    /// включая самого себя, и установка чинится только правкой базы руками.
    /// </summary>
    private async Task RefuseIfLastAdminAsync(User user, CancellationToken ct)
    {
        if (!user.IsAdmin || !user.IsActive)
            return;

        var others = await db.Users.CountAsync(
            u => u.IsAdmin && u.IsActive && u.Id != user.Id, ct);

        if (others == 0)
            throw new ValidationException("This is the last active administrator.");
    }

    private async Task RevokeTokensAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(t => t.SetProperty(token => token.RevokedAt, now), ct);
    }

    private static AdminUserDto Describe(User user) =>
        new(user.Id, user.Username, user.DisplayName, user.IsAdmin, user.IsActive, user.CreatedAt);
}
