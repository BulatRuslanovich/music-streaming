// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Infrastructure.Persistence;
using App.Dtos;
using Microsoft.EntityFrameworkCore;

namespace App.Common;

public static class ProjectionLookups
{
    public static async Task<Dictionary<Guid, TrackDto>> TracksByIdAsync(
        this ApplicationDbContext db, Guid userId, IEnumerable<Guid> trackIds, CancellationToken ct = default)
    {
        List<Guid> ids = [.. trackIds.Distinct()];
        if (ids.Count == 0)
            return [];

        return await db.Tracks.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .Select(ToDto.Track(userId))
            .ToDictionaryAsync(t => t.Id, ct);
    }
}
