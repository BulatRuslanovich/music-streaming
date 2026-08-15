using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

public class CatalogService(IApplicationDbContext db, ICurrentUser currentUser)
{
    public enum TrackSort { Title, Recent, Artist, Album }

    /// <summary>
    /// Сколько треков отдавать вперемешку. Больше очередь всё равно не переслушать за раз, а
    /// каждый трек в ней — это и байты по сети, и запись в localStorage при каждом переключении.
    /// </summary>
    public const int MaxShuffleTracks = 200;

    public async Task<PagedResult<TrackDto>> GetTracksAsync(
        PageRequest page,
        TrackSort sort = TrackSort.Title,
        string? search = null,
        CancellationToken ct = default)
    {
        var query = FilterTracks(search);

        var ordered = sort switch
        {
            TrackSort.Recent => query.OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Title),
            TrackSort.Artist => query.OrderBy(t => t.Artist!.Name).ThenBy(t => t.Title),
            TrackSort.Album => query.OrderBy(t => t.Album!.Title).ThenBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber),
            _ => query.OrderBy(t => t.Title).ThenBy(t => t.Artist!.Name),
        };

        return await ordered.ToPagedAsync(page, Projections.Track(currentUser.Id), ct);
    }

    /// <summary>
    /// Случайная выборка из всей библиотеки (или из того, что осталось после поиска), а не из
    /// показанной страницы: перемешивать полагается фонотеку целиком, сколько бы её ни было видно.
    /// Порядок задаёт база — клиенту незачем ради этого выкачивать весь список.
    /// </summary>
    public async Task<IReadOnlyList<TrackDto>> GetShuffledTracksAsync(
        int? limit = null, string? search = null, CancellationToken ct = default)
    {
        var take = limit is null or < 1 ? MaxShuffleTracks : Math.Min(limit.Value, MaxShuffleTracks);

        return await FilterTracks(search)
            .OrderBy(_ => EF.Functions.Random())
            .Take(take)
            .Select(Projections.Track(currentUser.Id))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Фильтр здесь, а не в <c>/search</c>, чтобы суженный список по-прежнему листался и
    /// сортировался — и чтобы перемешивание брало ровно то, что пользователь видит на странице.
    /// </summary>
    private IQueryable<Track> FilterTracks(string? search)
    {
        var query = db.Tracks.AsNoTracking();

        if (SearchTerm.For(search) is not { Pattern: var pattern }) return query;

        return query.Where(t =>
            EF.Functions.Like(t.NormalizedTitle, pattern, SearchTerm.EscapeChar)
            || t.TrackArtists.Any(ta =>
                EF.Functions.Like(ta.Artist!.NormalizedName, pattern, SearchTerm.EscapeChar))
            || (t.Album != null
                && EF.Functions.Like(t.Album.NormalizedTitle, pattern, SearchTerm.EscapeChar)));
    }

    public async Task<TrackDto> GetTrackAsync(Guid id, CancellationToken ct = default)
    {
        var track = await db.Tracks.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(Projections.Track(currentUser.Id))
            .FirstOrDefaultAsync(ct);

        return track ?? throw new NotFoundException("Track not found.");
    }

    public async Task<PagedResult<ArtistDto>> GetArtistsAsync(
        PageRequest page, string? search = null, CancellationToken ct = default)
    {
        var query = db.Artists.AsNoTracking();

        if (SearchTerm.For(search) is { Pattern: var pattern })
            query = query.Where(a => EF.Functions.Like(a.NormalizedName, pattern, SearchTerm.EscapeChar));

        return await query.OrderBy(a => a.Name).ToPagedAsync(page, Projections.Artist, ct);
    }

    public async Task<ArtistDetailDto> GetArtistAsync(
        Guid id, PageRequest? trackPage = null, CancellationToken ct = default)
    {
        var page = trackPage ?? new PageRequest();

        var artist = await db.Artists.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new { a.Id, a.Name, a.ImagePath })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Artist not found.");

        var albums = await db.Albums.AsNoTracking()
            .Where(a => a.ArtistId == id || a.Tracks.Any(t => t.TrackArtists.Any(ta => ta.ArtistId == id)))
            .OrderBy(a => a.Year == null)
            .ThenByDescending(a => a.Year)
            .ThenBy(a => a.Title)
            .Select(Projections.Album)
            .ToListAsync(ct);

        // Приглашённые участники тоже считаются: совместный трек виден у каждого названного им
        // исполнителя.
        var tracks = await db.Tracks.AsNoTracking()
            .Where(t => t.TrackArtists.Any(ta => ta.ArtistId == id))
            .OrderBy(t => t.Title)
            .ToPagedAsync(page, Projections.Track(currentUser.Id), ct);

        return new ArtistDetailDto(artist.Id, artist.Name, artist.ImagePath != null, albums, tracks);
    }

    public async Task<PagedResult<AlbumDto>> GetAlbumsAsync(
        PageRequest page,
        Guid? artistId = null,
        bool recentFirst = false,
        string? search = null,
        CancellationToken ct = default)
    {
        var query = db.Albums.AsNoTracking();
        if (artistId is not null)
            query = query.Where(a => a.ArtistId == artistId);

        if (SearchTerm.For(search) is { Pattern: var pattern })
        {
            query = query.Where(a =>
                EF.Functions.Like(a.NormalizedTitle, pattern, SearchTerm.EscapeChar)
                || EF.Functions.Like(a.Artist!.NormalizedName, pattern, SearchTerm.EscapeChar));
        }

        var ordered = recentFirst
            ? query.OrderByDescending(a => a.CreatedAt)
            : query.OrderBy(a => a.Title);

        return await ordered.ToPagedAsync(page, Projections.Album, ct);
    }

    public async Task<AlbumDetailDto> GetAlbumAsync(Guid id, CancellationToken ct = default)
    {
        var album = await db.Albums.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new
            {
                a.Id,
                a.Title,
                a.ArtistId,
                ArtistName = a.Artist!.Name,
                a.Year,
                HasCover = a.CoverPath != null,
                Duration = a.Tracks.Sum(t => t.DurationSeconds),
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Album not found.");

        var tracks = await db.Tracks.AsNoTracking()
            .Where(t => t.AlbumId == id)
            .OrderBy(t => t.DiscNumber ?? 1)
            .ThenBy(t => t.TrackNumber ?? int.MaxValue)
            .ThenBy(t => t.Title)
            .Select(Projections.Track(currentUser.Id))
            .ToListAsync(ct);

        return new AlbumDetailDto(
            album.Id, album.Title, album.ArtistId, album.ArtistName,
            album.Year, album.HasCover, album.Duration, tracks);
    }

    public async Task<IReadOnlyList<GenreDto>> GetGenresAsync(CancellationToken ct = default) =>
        await db.Genres.AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(Projections.Genre)
            .ToListAsync(ct);

    public async Task<PagedResult<TrackDto>> GetGenreTracksAsync(
        Guid genreId, PageRequest page, CancellationToken ct = default)
    {
        if (!await db.Genres.AnyAsync(g => g.Id == genreId, ct))
            throw new NotFoundException("Genre not found.");

        return await db.Tracks.AsNoTracking()
            .Where(t => t.GenreId == genreId)
            .OrderBy(t => t.Artist!.Name).ThenBy(t => t.Title)
            .ToPagedAsync(page, Projections.Track(currentUser.Id), ct);
    }

    public async Task<HomeSummaryDto> GetHomeSummaryAsync(int sectionSize = 12, CancellationToken ct = default)
    {
        var userId = currentUser.Id;
        var projectTrack = Projections.Track(userId);

        var recentlyAdded = await db.Tracks.AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .Take(sectionSize)
            .Select(projectTrack)
            .ToListAsync(ct);

        var lastPlays = await db.ListeningHistory.AsNoTracking()
            .Where(h => h.UserId == userId)
            .GroupBy(h => h.TrackId)
            .Select(g => new { TrackId = g.Key, PlayedAt = g.Max(h => h.PlayedAt) })
            .OrderByDescending(x => x.PlayedAt)
            .Take(sectionSize)
            .ToListAsync(ct);

        var playedIds = lastPlays.Select(x => x.TrackId).ToList();
        var playedTracks = await db.Tracks.AsNoTracking()
            .Where(t => playedIds.Contains(t.Id))
            .Select(projectTrack)
            .ToListAsync(ct);

        var recentlyPlayed = lastPlays
            .Join(playedTracks, x => x.TrackId, t => t.Id, (_, t) => t)
            .ToList();

        var favorites = await db.Favorites.AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Take(sectionSize)
            .Select(f => f.Track!)
            .Select(projectTrack)
            .ToListAsync(ct);

        var albums = await db.Albums.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .Take(sectionSize)
            .Select(Projections.Album)
            .ToListAsync(ct);

        var playlists = await db.Playlists.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.UpdatedAt)
            .Take(sectionSize)
            .Select(Projections.Playlist)
            .ToListAsync(ct);

        var stats = new LibraryStatsDto(
            await db.Tracks.CountAsync(ct),
            await db.Albums.CountAsync(ct),
            await db.Artists.CountAsync(ct),
            await db.Playlists.CountAsync(p => p.UserId == userId, ct),
            await db.Tracks.SumAsync(t => (long)t.DurationSeconds, ct),
            await db.Tracks.SumAsync(t => t.FileSize, ct));

        return new HomeSummaryDto(recentlyAdded, recentlyPlayed, favorites, albums, playlists, stats);
    }
}
