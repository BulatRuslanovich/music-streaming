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
        var touched = await TouchedByAsync([FactsOf(track)], ct);

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

    /// <summary>
    /// Сколько треков уходит за раз.
    ///
    /// <para>
    /// Ограничение не про базу — один оператор снёс бы и десять тысяч, — а про то, что каждый
    /// удалённый трек уносит с собой файл и до трёх перекодировок, и эти вызовы удаления держат
    /// поток запроса. Страница библиотеки показывает сотню, а выбор живёт в пределах страницы, так
    /// что двести — это запас вдвое, а не потолок, в который кто-то упрётся.
    /// </para>
    /// </summary>
    public const int MaxBulkDelete = 200;

    public async Task DeleteTrackAsync(Guid id, CancellationToken ct = default)
    {
        if ((await DeleteTracksAsync([id], ct)).Deleted == 0)
            throw new NotFoundException("Track not found.");
    }

    /// <summary>
    /// Удаляет набор треков разом.
    ///
    /// <para>
    /// Не цикл по одиночному удалению: сколько бы треков ни назвали, база спрашивается дважды —
    /// про сами треки и про их соавторов, — а удаление уходит одним оператором, каскады которого
    /// разбирает сама база. Уборка осиротевших альбомов и исполнителей тоже одна на весь набор:
    /// она и раньше работала со списком, просто список был из одного.
    /// </para>
    ///
    /// <para>
    /// Названное, но уже удалённое возвращается в <c>Missing</c> и ошибкой не считается: удалить
    /// удалённое — то же состояние, к которому шёл запрос.
    /// </para>
    /// </summary>
    public async Task<BulkDeleteResultDto> DeleteTracksAsync(
        IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        var wanted = ids.Distinct().ToList();

        if (wanted.Count == 0)
            throw new ValidationException("No tracks were selected.");

        if (wanted.Count > MaxBulkDelete)
            throw new ValidationException($"At most {MaxBulkDelete} tracks can be deleted at once.");

        // Всё, что понадобится после удаления, вычитывается до него: по исчезнувшей строке ни путей,
        // ни ссылок уже не узнать.
        var facts = await db.Tracks.AsNoTracking()
            .Where(t => wanted.Contains(t.Id))
            .Select(t => new TrackFacts(
                t.Id, t.ArtistId, t.AlbumId, t.GenreId, t.FilePath, t.ContentHash))
            .ToListAsync(ct);

        if (facts.Count == 0)
            return new BulkDeleteResultDto(0, wanted);

        var found = facts.Select(f => f.Id).ToList();
        var touched = await TouchedByAsync(facts, ct);

        var deleted = await db.Tracks.Where(t => found.Contains(t.Id)).ExecuteDeleteAsync(ct);

        // Файлы — только после того, как база подтвердила удаление: обратный порядок оставил бы
        // живые строки без звука, если бы удаление не прошло.
        foreach (var fact in facts)
        {
            storage.Delete(fact.FilePath);
            storage.DeleteTranscodes(fact.ContentHash);
        }

        await CleanUpOrphansAsync(touched, ct);

        logger.LogInformation(
            "Deleted {Deleted} tracks in one batch; {Missing} of the requested ids were already gone",
            deleted, wanted.Count - facts.Count);

        return new BulkDeleteResultDto(deleted, [.. wanted.Except(found)]);
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
    /// Альбомы, исполнители и жанры, на которые ссылались эти треки, — единственные, кого их правка
    /// или удаление способны осиротить.
    ///
    /// <para>
    /// Соавторы всего набора берутся одним запросом: на двести треков их спрашивают столько же раз,
    /// сколько на один.
    /// </para>
    /// </summary>
    private async Task<OrphanCandidates> TouchedByAsync(
        IReadOnlyCollection<TrackFacts> tracks, CancellationToken ct)
    {
        var trackIds = tracks.Select(t => t.Id).ToList();

        var credited = await db.TrackArtists
            .Where(ta => trackIds.Contains(ta.TrackId))
            .Select(ta => ta.ArtistId)
            .Distinct()
            .ToListAsync(ct);

        return new OrphanCandidates(
            [.. tracks.Select(t => t.AlbumId).OfType<Guid>().Distinct()],
            [.. credited.Concat(tracks.Select(t => t.ArtistId)).Distinct()],
            [.. tracks.Select(t => t.GenreId).OfType<Guid>().Distinct()]);
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

    /// <summary>Всё, что о треке нужно знать после того, как его строки уже нет.</summary>
    /// <param name="FilePath">Спросить его по удалённой строке негде, поэтому он запоминается заранее.</param>
    private readonly record struct TrackFacts(
        Guid Id, Guid ArtistId, Guid? AlbumId, Guid? GenreId, string FilePath, string ContentHash);

    private static TrackFacts FactsOf(Track track) =>
        new(track.Id, track.ArtistId, track.AlbumId, track.GenreId, track.FilePath, track.ContentHash);
}
