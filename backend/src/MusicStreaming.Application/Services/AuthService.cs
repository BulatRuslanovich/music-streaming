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

        // Отозванный токен предъявлен снова. Каждое обновление отзывает свой токен и выдаёт
        // новый, поэтому у отозванного есть ровно два объяснения — и они требуют разного.
        if (stored is { RevokedAt: { } revokedAt })
        {
            // Первое: две вкладки одного браузера упёрлись в 401 одновременно и обе пошли
            // продлеваться со старой кукой — вторая пришла с токеном, который первая только что
            // отозвала. Это гонка, а не кража, и выкидывать за неё из приложения обеих нельзя:
            // внутри короткого окна такой токен ещё раз проворачивается как обычно.
            //
            // Одного возраста для этого мало. Отзыв бывает не только ротацией: деактивация,
            // смена пароля и разбор кражи ниже гасят всю цепочку разом, и такой токен тоже
            // «только что отозван». Отличает гонку то, что после ротации сессия продолжает
            // жить — где-то есть действующий токен, — а после гашения не остаётся ни одного.
            // Без этой проверки защита отменяла бы сама себя на длину окна.
            var sessionLivesOn = await db.RefreshTokens.AnyAsync(
                t => t.UserId == stored.UserId && t.RevokedAt == null && t.ExpiresAt > now, ct);

            if (!sessionLivesOn || now - revokedAt > ReuseGrace)
            {
                // Второе: копию цепочки продолжает кто-то ещё. Кто из двоих настоящий клиент,
                // отсюда не видно, поэтому закрываются оба — войти заново сможет тот, у кого
                // есть пароль, а не тот, у кого есть только токен.
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

        // Деактивация отзывает выданные токены, но проверка здесь всё равно нужна: обновление —
        // единственная точка, где сессия продлевается, и закрыть её значит закрыть доступ навсегда,
        // как бы токен ни оказался на руках.
        //
        // Срок жизни проверяется отдельно от отзыва: отозванный внутри окна выше уже разобран.
        if (stored?.User is null || stored.ExpiresAt <= now || !stored.User.IsActive)
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

    /// <summary>
    /// Сколько после отзыва токен ещё считается своим. Ровно столько, чтобы покрыть одновременное
    /// продление из двух вкладок, и слишком мало, чтобы этим окном пользовался кто-то ещё.
    /// </summary>
    private static readonly TimeSpan ReuseGrace = TimeSpan.FromSeconds(20);
}
