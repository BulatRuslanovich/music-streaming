using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Infrastructure.Persistence;

/// <summary>
/// Приводит базу в рабочее состояние при старте: накатывает миграции и обеспечивает существование
/// учётной записи владельца.
///
/// <para>
/// Миграции применяет само приложение, а не отдельный шаг развёртывания. Установка здесь — это
/// «заполните .env и запустите compose», и любой дополнительный шаг был бы шагом, который забудут:
/// приложение поднялось бы на устаревшей схеме.
/// </para>
/// </summary>
public class DatabaseInitializer(
    ApplicationDbContext db,
    IPasswordHasher passwordHasher,
    IConfiguration configuration,
    ILogger<DatabaseInitializer> logger)
{
    /// <summary>Накатывает миграции, затем заводит или восстанавливает владельца.</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await MigrateWithRetryAsync(ct);
        await SeedOwnerAsync(ct);
    }

    /// <summary>
    /// Накатывает миграции, переживая ещё не готовую базу.
    ///
    /// <para>
    /// В compose Postgres и приложение стартуют вместе, и проверки живости не всегда достаточно:
    /// контейнер отвечает раньше, чем сервер начинает принимать соединения. Последняя попытка
    /// делается уже без перехвата — чтобы упасть с настоящей ошибкой, а не с обобщённой.
    /// </para>
    /// </summary>
    private async Task MigrateWithRetryAsync(CancellationToken ct)
    {
        const int maxAttempts = 12;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync(ct);
                logger.LogInformation("Database schema is up to date");
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(5, attempt));
                logger.LogWarning(
                    "Database not ready (attempt {Attempt}/{Max}): {Message}. Retrying in {Delay}s",
                    attempt, maxAttempts, ex.Message, delay.TotalSeconds);

                await Task.Delay(delay, ct);
            }
        }

        await db.Database.MigrateAsync(ct);
    }

    /// <summary>
    /// Заводит учётную запись владельца, если её нет, и всегда возвращает ей права.
    ///
    /// <para>
    /// Восстановление прав на каждом старте — это выход из самоблокировки: администратор может снять
    /// права сам с себя или деактивировать собственную запись, и в self-hosted установке помочь
    /// будет некому. Инструкция «поправьте .env и перезапустите контейнер» работает без всяких
    /// предварительных знаний.
    /// </para>
    ///
    /// <para>
    /// Пароль при этом не трогается без явного разрешения: иначе он возвращался бы к значению из
    /// настроек при каждом перезапуске, обесценивая любую его смену.
    /// </para>
    /// </summary>
    private async Task SeedOwnerAsync(CancellationToken ct)
    {
        var username = (configuration["Owner:Username"] ?? "admin").Trim().ToLowerInvariant();
        var password = configuration["Owner:Password"];

        var existing = await db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
        if (existing is not null)
        {
            // Учётная запись владельца всегда возвращается действующим администратором: это
            // единственный путь назад, если администраторы заперли сами себя.
            if (!existing.IsAdmin || !existing.IsActive)
            {
                existing.IsAdmin = true;
                existing.IsActive = true;
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Restored administrator access for the owner account {Username}", username);
            }

            if (!string.IsNullOrWhiteSpace(password) &&
                configuration.GetValue("Owner:ResetPasswordOnStartup", false))
            {
                existing.PasswordHash = passwordHasher.Hash(password);
                await db.SaveChangesAsync(ct);
                logger.LogWarning("Password for user {Username} was reset from configuration", username);
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "No user exists yet and Owner:Password is not configured. " +
                "Set OWNER__PASSWORD (see .env.example) so the first account can be created.");
        }

        if (password.Length < 8)
            throw new InvalidOperationException("Owner:Password must be at least 8 characters long.");

        var displayName = configuration["Owner:DisplayName"];

        db.Users.Add(new User
        {
            Username = username,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? username : displayName.Trim(),
            PasswordHash = passwordHasher.Hash(password),
            IsAdmin = true,
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Created initial user {Username}", username);
    }
}
