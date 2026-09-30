// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

public class UserSettingsService(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    private UserSettings? loaded;

    public async Task<UserSettings> GetAsync(CancellationToken ct)
    {
        if (loaded is not null)
            return loaded;

        loaded = await db.UserSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == currentUser.Id, ct)
                ?? new UserSettings { UserId = currentUser.Id };

        return loaded;
    }

    public async Task<UserSettingsDto> UpdateAsync(
        UpdateUserSettingsRequest request, CancellationToken ct)
    {
        var settings = await db.UserSettings.FirstOrDefaultAsync(s => s.UserId == currentUser.Id, ct);

        if (settings is null)
        {
            settings = new UserSettings { UserId = currentUser.Id };
            db.UserSettings.Add(settings);
        }

        if (request.Quality is { } quality)
            settings.Quality = Enum.IsDefined(quality) ? quality : throw new ValidationException("Unknown audio quality.");

        if (request.DataSaver is { } dataSaver)
            settings.DataSaver = dataSaver;

        if (request.TimeZone?.Trim() is { } timeZone)
        {
            if (timeZone.Length is 0 or > 64)
                throw new ValidationException("The time zone name is not valid.");

            var known = await db.Database
                .SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM pg_timezone_names WHERE name = {timeZone}) AS \"Value\"")
                .SingleAsync(ct);

            settings.TimeZone = known ? timeZone : throw new ValidationException($"Unknown time zone '{timeZone}'.");
        }

        settings.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        loaded = settings;

        return ToDto(settings);
    }

    public static UserSettingsDto ToDto(UserSettings settings) =>
        new(settings.Quality, settings.DataSaver, settings.TimeZone);
}
