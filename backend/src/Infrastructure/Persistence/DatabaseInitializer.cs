// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using App.Abstractions;
using Domain.Common;
using Domain.Entities;

namespace Infrastructure.Persistence;

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
        await SeedOwnerAsync(ct);
    }

    private async Task WaitForDatabaseAsync(CancellationToken ct)
    {
        const int maxAttempts = 12;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
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

        db.Users.Add(new User
        {
            Username = username,
            PasswordHash = passwordHasher.Hash(password),
            IsAdmin = true,
            CreatedAt = clock.GetUtcNow(),
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Created initial user {Username}", username);
    }
}
