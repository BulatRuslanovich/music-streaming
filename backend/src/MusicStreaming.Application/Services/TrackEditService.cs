using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Common;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

public class TrackEditService(
    IApplicationDbContext db,
    IMusicStorage storage,
    TagResolver tags,
    CatalogService catalog,
    ILogger<TrackEditService> logger)
{
    public async Task<TrackDto> UpdateTrackAsync(
        Guid id, UpdateTrackRequest request, CancellationToken ct = default)
    {
        var track = await db.Tracks.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("Track not found.");

        // До правки: после неё связь уже разорвана, и по треку не узнать, из какого альбома он ушёл.
        var touched = await TouchedByAsync(track, ct);

        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            track.Title = request.Title.Trim();
            track.NormalizedTitle = Normalize.Key(track.Title);
        }

        if (!string.IsNullOrWhiteSpace(request.Artist))
        {
            var artists = await tags.ResolveArtistsAsync([request.Artist], ct);
            track.ArtistId = artists[0].Id;
            await SetTrackArtistsAsync(track, artists, ct);
        }

        if (request.Album is not null)
        {
            track.AlbumId = string.IsNullOrWhiteSpace(request.Album)
                ? null
                : (await tags.GetOrCreateAlbumAsync(request.Album, track.ArtistId, request.Year ?? track.Year, ct)).Id;
        }

        if (request.Genre is not null)
        {
            track.GenreId = string.IsNullOrWhiteSpace(request.Genre)
                ? null
                : (await tags.GetOrCreateGenreAsync(request.Genre, ct)).Id;
        }

        if (request.Year is not null) track.Year = request.Year;
        if (request.TrackNumber is not null) track.TrackNumber = request.TrackNumber;
        if (request.DiscNumber is not null) track.DiscNumber = request.DiscNumber;

        await db.SaveChangesAsync(ct);
        await CleanUpOrphansAsync(touched, ct);

        logger.LogInformation("Track {TrackId} metadata updated", id);
        return await catalog.GetTrackAsync(id, ct);
    }

    public async Task DeleteTrackAsync(Guid id, CancellationToken ct = default)
    {
        var track = await db.Tracks.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("Track not found.");

        var filePath = track.FilePath;
        var contentHash = track.ContentHash;
        var touched = await TouchedByAsync(track, ct);

        db.Tracks.Remove(track);
        await db.SaveChangesAsync(ct);

        storage.Delete(filePath);
        storage.DeleteTranscodes(contentHash);
        await CleanUpOrphansAsync(touched, ct);

        logger.LogInformation("Track {TrackId} deleted along with {FilePath}", id, filePath);
    }

    private async Task SetTrackArtistsAsync(Track track, IReadOnlyList<Artist> artists, CancellationToken ct)
    {
        var existing = await db.TrackArtists.Where(ta => ta.TrackId == track.Id).ToListAsync(ct);
        var wanted = artists.Select(a => a.Id).ToList();

        db.TrackArtists.RemoveRange(existing.Where(link => !wanted.Contains(link.ArtistId)));

        for (var position = 0; position < wanted.Count; position++)
        {
            var link = existing.FirstOrDefault(l => l.ArtistId == wanted[position]);
            if (link is null)
                db.TrackArtists.Add(new TrackArtist { TrackId = track.Id, ArtistId = wanted[position], Position = position });
            else
                link.Position = position;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Альбом, исполнители и жанр, на которых трек ссылался, — единственные, кого его правка или
    /// удаление способны осиротить.
    /// </summary>
    private async Task<OrphanCandidates> TouchedByAsync(Track track, CancellationToken ct)
    {
        var credited = await db.TrackArtists
            .Where(ta => ta.TrackId == track.Id)
            .Select(ta => ta.ArtistId)
            .ToListAsync(ct);

        return new OrphanCandidates(
            track.AlbumId is { } albumId ? [albumId] : [],
            [.. credited.Append(track.ArtistId).Distinct()],
            track.GenreId is { } genreId ? [genreId] : []);
    }

    /// <summary>
    /// Убирает то, что осталось без единого трека.
    ///
    /// <para>
    /// Проверяются только сущности, которых коснулась эта правка, а не вся библиотека: осиротеть от
    /// изменения одного трека способны лишь те, на кого он ссылался. Прежний проход перебирал все
    /// альбомы и всех исполнителей на каждое редактирование, и стоил тем дороже, чем больше
    /// фонотека. Оставшееся от других путей (например, от загрузки, упавшей на полпути) подбирает
    /// плановый проход обслуживания.
    /// </para>
    /// </summary>
    private async Task CleanUpOrphansAsync(OrphanCandidates touched, CancellationToken ct)
    {
        var candidateArtists = touched.ArtistIds;

        // Альбомы уходят раньше, чем проверяются исполнители: исполнитель считается осиротевшим,
        // только когда у него не осталось и альбомов тоже.
        if (touched.AlbumIds.Count > 0)
        {
            var albumIds = touched.AlbumIds;
            var emptyAlbums = await db.Albums
                .Where(a => albumIds.Contains(a.Id) && !a.Tracks.Any())
                .ToListAsync(ct);

            foreach (var album in emptyAlbums)
            {
                if (album.CoverPath is not null)
                    storage.DeleteCover(album.CoverPath);
                db.Albums.Remove(album);
            }

            if (emptyAlbums.Count > 0)
                await db.SaveChangesAsync(ct);

            // Исполнитель самого альбома — не обязательно исполнитель трека: у сборника это
            // «Various Artists», которого не назвал ни один из её треков. Уходя, альбом способен
            // осиротить его, поэтому в проверку он попадает вместе со своим альбомом.
            candidateArtists = [.. candidateArtists.Concat(emptyAlbums.Select(a => a.ArtistId)).Distinct()];
        }

        if (candidateArtists.Count > 0)
        {
            var artistIds = candidateArtists;
            var emptyArtists = await db.Artists
                .Where(a => artistIds.Contains(a.Id)
                            && !a.Tracks.Any() && !a.Albums.Any() && !a.TrackCredits.Any())
                .ToListAsync(ct);

            foreach (var artist in emptyArtists.Where(a => a.ImagePath is not null))
                storage.Delete(artist.ImagePath!);

            db.Artists.RemoveRange(emptyArtists);

            if (emptyArtists.Count > 0)
                await db.SaveChangesAsync(ct);
        }

        if (touched.GenreIds.Count > 0)
        {
            var genreIds = touched.GenreIds;
            await db.Genres
                .Where(g => genreIds.Contains(g.Id) && !g.Tracks.Any())
                .ExecuteDeleteAsync(ct);
        }
    }

    /// <summary>Сущности, которых правка трека могла оставить без единого трека.</summary>
    /// <param name="AlbumIds">Альбом, из которого трек ушёл или в котором был.</param>
    /// <param name="ArtistIds">Основной исполнитель и все соавторы, названные до правки.</param>
    /// <param name="GenreIds">Жанр, указанный до правки.</param>
    private readonly record struct OrphanCandidates(
        List<Guid> AlbumIds,
        List<Guid> ArtistIds,
        List<Guid> GenreIds);
}
