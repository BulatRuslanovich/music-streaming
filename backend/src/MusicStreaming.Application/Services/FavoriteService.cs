using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

public sealed class FavoriteService(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<PagedResult<TrackDto>> GetFavoritesAsync(PageRequest page, CancellationToken ct = default)
    {
        var query = db.Favorites.AsNoTracking().Where(f => f.UserId == currentUser.Id);
        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(f => f.CreatedAt)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(f => f.Track!)
            .Select(Projections.Track(currentUser.Id))
            .ToListAsync(ct);

        return new PagedResult<TrackDto>(items, total, page.Page, page.PageSize);
    }

    /// <summary>Idempotent: favouriting an already-favourited track is a no-op, not an error.</summary>
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
