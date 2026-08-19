// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Common;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

public class UserSettingsService(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<UserSettings> GetAsync(CancellationToken ct = default) =>
        await db.UserSettings.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == currentUser.Id, ct)
        ?? new UserSettings { UserId = currentUser.Id };

    public async Task<UserSettingsDto> UpdateAsync(
        UpdateUserSettingsRequest request, CancellationToken ct = default)
    {
        var settings = await db.UserSettings.FirstOrDefaultAsync(s => s.UserId == currentUser.Id, ct);
        if (settings is null)
        {
            settings = new UserSettings { UserId = currentUser.Id };
            db.UserSettings.Add(settings);
        }

        if (request.Autoplay is { } autoplay)
            settings.Autoplay = autoplay;

        if (request.Quality is { } quality)
            settings.Quality = Enum.IsDefined(quality) ? quality : throw new ValidationException("Unknown audio quality.");

        if (request.DataSaver is { } dataSaver)
            settings.DataSaver = dataSaver;

        if (request.TimeZone is { } timeZone)
            settings.TimeZone = await ValidateTimeZoneAsync(timeZone, ct);

        settings.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        return Describe(settings);
    }

    public static UserSettingsDto Describe(UserSettings settings) =>
        new(settings.Autoplay, settings.Quality, settings.DataSaver, settings.TimeZone);

    private async Task<string> ValidateTimeZoneAsync(string timeZone, CancellationToken ct)
    {
        var candidate = timeZone.Trim();

        if (candidate.Length is 0 or > 64)
            throw new ValidationException("The time zone name is not valid.");

        var known = await db.Database
            .SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM pg_timezone_names WHERE name = {candidate}) AS \"Value\"")
            .SingleAsync(ct);

        return known ? candidate : throw new ValidationException($"Unknown time zone '{candidate}'.");
    }
}
