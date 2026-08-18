using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Infrastructure.Persistence;

public class DatabaseInitializer(
    ApplicationDbContext db,
    IPasswordHasher passwordHasher,
    IConfiguration configuration,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await MigrateWithRetryAsync(ct);
        await SeedOwnerAsync(ct);
    }

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
                var delay = TimeSpan.FromSeconds(5);
                logger.LogWarning(
                    "Database not ready (attempt {Attempt}/{Max}): {Message}.",
                    attempt, maxAttempts, ex.Message);

                await Task.Delay(delay, ct);
            }
        }

        await db.Database.MigrateAsync(ct);
    }

    private async Task SeedOwnerAsync(CancellationToken ct)
    {
        var username = (configuration["Owner:Username"] ?? "admin").Trim().ToLowerInvariant();
        var password = configuration["Owner:Password"];

        var existing = await db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
        if (existing is not null)
        {
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
