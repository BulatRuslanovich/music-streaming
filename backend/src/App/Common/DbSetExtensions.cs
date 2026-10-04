// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Domain.Entities;

namespace App.Common;

public static class DbSetExtensions
{
    public static async Task RequireTrackAsync(
        this ApplicationDbContext db, Guid trackId, CancellationToken ct = default)
    {
        if (!await db.Tracks.AnyAsync(t => t.Id == trackId, ct))
            throw new NotFoundException("Track not found.");
    }

    // Deleting (not just revoking) also kills the access tokens: their "sid" must point at a live row.
    public static Task<int> RevokeAllAsync(
        this IQueryable<RefreshToken> tokens, Guid userId, CancellationToken ct = default) =>
        tokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(ct);
}
