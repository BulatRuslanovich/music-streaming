// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;

namespace MusicStreaming.Application.Services;

public class FavoriteService(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<PagedResult<TrackDto>> GetFavoritesAsync(PageRequest page, CancellationToken ct)
    {
        return await db.Favorites.AsNoTracking()
            .Where(f => f.UserId == currentUser.Id)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => f.Track!)
            .ToPagedAsync(page, ToDto.Track(currentUser.Id), ct);
    }

    public async Task AddAsync(Guid trackId, CancellationToken ct)
    {
        await db.RequireTrackAsync(trackId, ct);

        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO favorites (user_id, track_id, created_at)
            VALUES ({currentUser.Id}, {trackId}, {clock.GetUtcNow()})
            ON CONFLICT (user_id, track_id) DO NOTHING
            """, ct);
    }

    public async Task RemoveAsync(Guid trackId, CancellationToken ct)
    {
        var favorite = await db.Favorites
            .FirstOrDefaultAsync(f => f.UserId == currentUser.Id && f.TrackId == trackId, ct);

        if (favorite is null)
            return;

        db.Favorites.Remove(favorite);
        await db.SaveChangesAsync(ct);
    }
}
