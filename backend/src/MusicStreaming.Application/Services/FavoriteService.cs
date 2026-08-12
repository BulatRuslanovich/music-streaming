using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

public class FavoriteService(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<PagedResult<TrackDto>> GetFavoritesAsync(PageRequest page, CancellationToken ct = default)
    {
        return await db.Favorites.AsNoTracking()
            .Where(f => f.UserId == currentUser.Id)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => f.Track!)
            .ToPagedAsync(page, Projections.Track(currentUser.Id), ct);
    }

    public async Task AddAsync(Guid trackId, CancellationToken ct = default)
    {
        if (!await db.Tracks.AnyAsync(t => t.Id == trackId, ct))
            throw new NotFoundException("Track not found.");

        var exists = await db.Favorites
            .AnyAsync(f => f.UserId == currentUser.Id && f.TrackId == trackId, ct);

        if (exists)
            return;

        db.Favorites.Add(new Favorite
        {
            UserId = currentUser.Id,
            TrackId = trackId,
            CreatedAt = clock.GetUtcNow(),
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Guid trackId, CancellationToken ct = default)
    {
        var favorite = await db.Favorites
            .FirstOrDefaultAsync(f => f.UserId == currentUser.Id && f.TrackId == trackId, ct);

        if (favorite is null)
            return;

        db.Favorites.Remove(favorite);
        await db.SaveChangesAsync(ct);
    }
}
