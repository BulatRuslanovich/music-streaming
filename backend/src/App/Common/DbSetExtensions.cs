// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Abstractions;
using Microsoft.EntityFrameworkCore;
using Domain.Entities;

namespace App.Common;

public static class DbSetExtensions
{
    public static async Task RequireTrackAsync(
        this IApplicationDbContext db, Guid trackId, CancellationToken ct = default)
    {
        if (!await db.Tracks.AnyAsync(t => t.Id == trackId, ct))
            throw new NotFoundException("Track not found.");
    }

    public static Task<int> RevokeAllAsync(
        this IQueryable<RefreshToken> tokens,
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        tokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(t => t.SetProperty(token => token.RevokedAt, now), ct);
}
