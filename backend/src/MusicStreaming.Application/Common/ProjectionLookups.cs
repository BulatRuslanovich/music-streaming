using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Dtos;

namespace MusicStreaming.Application.Common;

/// <summary>
/// Подъём набора идентификаторов до готовых DTO — одним запросом и словарём на выходе.
///
/// <para>
/// Форма, к которой приходит всякий, кто сначала выбрал идентификаторы (полка рекомендаций,
/// страница истории, топ статистики, ответ проверки загрузки), а метаданные к ним досыпает вторым
/// запросом. Порядок словарь не хранит намеренно: он задан тем списком, из которого пришли
/// идентификаторы, и вызывающий раскладывает результат по нему сам — заодно выбрасывая то, что
/// успели удалить между двумя запросами.
/// </para>
///
/// <para>
/// Пустой набор до базы не доходит: <c>IN ()</c> не стоит round-trip'а.
/// </para>
/// </summary>
public static class ProjectionLookups
{
    /// <summary>Треки по идентификаторам, с персональными полями (отметка «избранное») для <paramref name="userId"/>.</summary>
    public static async Task<Dictionary<Guid, TrackDto>> TracksByIdAsync(
        this IApplicationDbContext db, Guid userId, IEnumerable<Guid> trackIds, CancellationToken ct = default)
    {
        var ids = Distinct(trackIds);
        if (ids.Count == 0)
            return [];

        return await db.Tracks.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .Select(Projections.Track(userId))
            .ToDictionaryAsync(t => t.Id, ct);
    }

    /// <summary>Исполнители по идентификаторам.</summary>
    public static async Task<Dictionary<Guid, ArtistDto>> ArtistsByIdAsync(
        this IApplicationDbContext db, IEnumerable<Guid> artistIds, CancellationToken ct = default)
    {
        var ids = Distinct(artistIds);
        if (ids.Count == 0)
            return [];

        return await db.Artists.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(Projections.Artist)
            .ToDictionaryAsync(a => a.Id, ct);
    }

    /// <summary>Альбомы по идентификаторам.</summary>
    public static async Task<Dictionary<Guid, AlbumDto>> AlbumsByIdAsync(
        this IApplicationDbContext db, IEnumerable<Guid> albumIds, CancellationToken ct = default)
    {
        var ids = Distinct(albumIds);
        if (ids.Count == 0)
            return [];

        return await db.Albums.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(Projections.Album)
            .ToDictionaryAsync(a => a.Id, ct);
    }

    private static List<Guid> Distinct(IEnumerable<Guid> ids) => [.. ids.Distinct()];
}
