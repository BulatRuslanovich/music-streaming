// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Domain.Common;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Infrastructure.Persistence;

public class DatabaseInitializer(
    ApplicationDbContext db,
    IPasswordHasher passwordHasher,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await WaitForDatabaseAsync(ct);
        await VerifySchemaAsync(ct);
        await SeedOwnerAsync(ct);
    }

    private async Task WaitForDatabaseAsync(CancellationToken ct)
    {
        const int maxAttempts = 12;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                // Пока идут скрипты из db/init, postgres слушает только свой сокет, а снаружи
                // порт закрыт, — так что ожидание базы заодно ждёт и создания схемы.
                if (await db.Database.CanConnectAsync(ct))
                    return;

                logger.LogWarning("Database is not accepting connections (attempt {Attempt}/{Max})",
                    attempt, maxAttempts);
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.LogWarning(
                    "Database not ready (attempt {Attempt}/{Max}): {Message}.",
                    attempt, maxAttempts, ex.Message);
            }

            if (attempt < maxAttempts)
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }

        throw new InvalidOperationException(
            "The database did not accept connections. Check that postgres is running and that " +
            "ConnectionStrings:Default points at it.");
    }

    private async Task VerifySchemaAsync(CancellationToken ct)
    {
        var missing = await SchemaGuard.FindMissingObjectsAsync(db, ct);
        if (missing.Count == 0)
        {
            logger.LogInformation("Database schema matches the model");
            return;
        }

        throw new InvalidOperationException(
            $"The database is missing {missing.Count} object(s) this version needs: " +
            $"{string.Join(", ", missing.Take(20))}{(missing.Count > 20 ? ", …" : string.Empty)}. " +
            "The schema is not created by the application — see db/README.md: a new database is " +
            "built by the scripts in db/init, an existing one is changed by hand.");
    }

    private async Task SeedOwnerAsync(CancellationToken ct)
    {
        var username = Normalize.Username(configuration["Owner:Username"] ?? "admin");
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
            CreatedAt = clock.GetUtcNow(),
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Created initial user {Username}", username);
    }
}
