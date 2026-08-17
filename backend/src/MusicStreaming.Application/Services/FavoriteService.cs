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

    /// <summary>
    /// Ставит отметку «избранное».
    ///
    /// <para>
    /// Одна вставка вместо «проверить и вставить»: проверка гонялась бы сама с собой, и двойной
    /// клик — а плеер отправляет запрос на каждое нажатие — упирался бы в первичный ключ, который
    /// наружу выходит как 500. Повторная отметка по смыслу и есть то состояние, к которому шёл
    /// запрос, поэтому конфликт здесь — успех, а не ошибка.
    /// </para>
    /// </summary>
    public async Task AddAsync(Guid trackId, CancellationToken ct = default)
    {
        if (!await db.Tracks.AnyAsync(t => t.Id == trackId, ct))
            throw new NotFoundException("Track not found.");

        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO favorites (user_id, track_id, created_at)
            VALUES ({currentUser.Id}, {trackId}, {clock.GetUtcNow()})
            ON CONFLICT (user_id, track_id) DO NOTHING
            """, ct);
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
