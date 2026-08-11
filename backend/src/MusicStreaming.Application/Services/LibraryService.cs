using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Options;
using MusicStreaming.Domain.Common;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

public class LibraryService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IMusicStorage storage,
    IImageProcessor imageProcessor,
    IOptions<StorageOptions> storageOptions,
    ILogger<LibraryService> logger)
{
    public enum TrackSort { Title, Recent, Artist, Album }

    private const int ArtistImageEdge = 640;

    private static readonly string[] AllowedImageContentTypes = ["image/jpeg", "image/png", "image/webp"];
    private static readonly string[] AllowedImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public async Task<PagedResult<TrackDto>> GetTracksAsync(
        PageRequest page,
        TrackSort sort = TrackSort.Title,
        CancellationToken ct = default)
    {
        var query = db.Tracks.AsNoTracking();
        var total = await query.CountAsync(ct);

        query = sort switch
        {
            TrackSort.Recent => query.OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Title),
            TrackSort.Artist => query.OrderBy(t => t.Artist!.Name).ThenBy(t => t.Title),
            TrackSort.Album => query.OrderBy(t => t.Album!.Title).ThenBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber),
            _ => query.OrderBy(t => t.Title).ThenBy(t => t.Artist!.Name),
        };

        var items = await query
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(Projections.Track(currentUser.Id))
            .ToListAsync(ct);

        return new PagedResult<TrackDto>(items, total, page.Page, page.PageSize);
    }

    public async Task<TrackDto> GetTrackAsync(Guid id, CancellationToken ct = default)
    {
        var track = await db.Tracks.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(Projections.Track(currentUser.Id))
            .FirstOrDefaultAsync(ct);

        return track ?? throw new NotFoundException("Track not found.");
    }

    public async Task<PagedResult<ArtistDto>> GetArtistsAsync(PageRequest page, CancellationToken ct = default)
    {
        var query = db.Artists.AsNoTracking();
        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(a => a.Name)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(Projections.Artist)
            .ToListAsync(ct);

        return new PagedResult<ArtistDto>(items, total, page.Page, page.PageSize);
    }

    public async Task<ArtistDetailDto> GetArtistAsync(Guid id, CancellationToken ct = default)
    {
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

        // Featured credits count: a collaboration is listed on every artist it names.
        var tracks = await db.Tracks.AsNoTracking()
            .Where(t => t.TrackArtists.Any(ta => ta.ArtistId == id))
            .OrderBy(t => t.Title)
            .Select(Projections.Track(currentUser.Id))
            .ToListAsync(ct);

        return new ArtistDetailDto(artist.Id, artist.Name, artist.ImagePath != null, albums, tracks);
    }

    public async Task<PagedResult<AlbumDto>> GetAlbumsAsync(
        PageRequest page,
        Guid? artistId = null,
        bool recentFirst = false,
        CancellationToken ct = default)
    {
        var query = db.Albums.AsNoTracking();
        if (artistId is not null)
            query = query.Where(a => a.ArtistId == artistId);

        var total = await query.CountAsync(ct);

        query = recentFirst
            ? query.OrderByDescending(a => a.CreatedAt)
            : query.OrderBy(a => a.Title);

        var items = await query
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(Projections.Album)
            .ToListAsync(ct);

        return new PagedResult<AlbumDto>(items, total, page.Page, page.PageSize);
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

        var query = db.Tracks.AsNoTracking().Where(t => t.GenreId == genreId);
        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(t => t.Artist!.Name).ThenBy(t => t.Title)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(Projections.Track(currentUser.Id))
            .ToListAsync(ct);

        return new PagedResult<TrackDto>(items, total, page.Page, page.PageSize);
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
            .Join(playedTracks, x => x.TrackId, t => t.Id, (x, t) => t)
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
            .Select(p => new PlaylistDto(
                p.Id, p.Name, p.Description, p.Tracks.Count,
                p.Tracks.Sum(pt => pt.Track!.DurationSeconds), p.CreatedAt, p.UpdatedAt))
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

    public async Task<ArtistDto> UpdateArtistAsync(
        Guid id, UpdateArtistRequest request, CancellationToken ct = default)
    {
        var artist = await db.Artists.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException("Artist not found.");

        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            throw new ValidationException("An artist needs a name.");
        if (name.Length > 300)
            throw new ValidationException("That name is longer than 300 characters.");

        var key = Normalize.Key(name);

        if (key != artist.NormalizedName &&
            await db.Artists.AnyAsync(a => a.NormalizedName == key && a.Id != id, ct))
        {
            throw new ConflictException($"An artist named \"{name}\" already exists.");
        }

        artist.Name = name;
        artist.NormalizedName = key;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException($"An artist named \"{name}\" already exists.");
        }

        logger.LogInformation("Artist {ArtistId} renamed to {Name}", id, name);
        return await ProjectArtistAsync(id, ct);
    }

    public async Task<ArtistDto> SetArtistImageAsync(
        Guid id,
        Stream content,
        string? contentType,
        string fileName,
        long length,
        CancellationToken ct = default)
    {
        var artist = await db.Artists.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException("Artist not found.");

        var maxBytes = storageOptions.Value.MaxImageUploadBytes;
        if (length > maxBytes)
            throw new UploadTooLargeException(maxBytes);

        if (contentType is null || !AllowedImageContentTypes.Contains(contentType.ToLowerInvariant()))
            throw new ValidationException("Only JPEG, PNG and WebP images are accepted.");

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedImageExtensions.Contains(extension))
            throw new ValidationException("Only .jpg, .png and .webp files are accepted.");

        using var buffered = new MemoryStream();
        await content.CopyToAsync(buffered, ct);
        buffered.Position = 0;

        var webp = await imageProcessor.ToSquareWebpAsync(buffered, ArtistImageEdge, ct);

        artist.ImagePath = await storage.SaveArtistImageAsync(artist.Id, webp, ct);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Photo set for artist {ArtistId} ({Bytes} bytes)", id, webp.Length);
        return await ProjectArtistAsync(id, ct);
    }

    public async Task RemoveArtistImageAsync(Guid id, CancellationToken ct = default)
    {
        var artist = await db.Artists.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException("Artist not found.");

        var path = artist.ImagePath;
        if (path is null)
            return;

        artist.ImagePath = null;
        await db.SaveChangesAsync(ct);

        storage.Delete(path);
        logger.LogInformation("Photo removed from artist {ArtistId}", id);
    }

    private Task<ArtistDto> ProjectArtistAsync(Guid id, CancellationToken ct) =>
        db.Artists.AsNoTracking().Where(a => a.Id == id).Select(Projections.Artist).FirstAsync(ct);

    public async Task<TrackDto> UpdateTrackAsync(Guid id, UpdateTrackRequest request, CancellationToken ct = default)
    {
        var track = await db.Tracks.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("Track not found.");

        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            track.Title = request.Title.Trim();
            track.NormalizedTitle = Normalize.Key(track.Title);
        }

        if (!string.IsNullOrWhiteSpace(request.Artist))
        {
            var artists = await ResolveArtistsAsync([request.Artist], ct);
            track.ArtistId = artists[0].Id;
            await db.SaveChangesAsync(ct); // any new artist needs its id before it is credited
            await SetTrackArtistsAsync(track, artists, ct);
        }

        if (request.Album is not null)
        {
            track.AlbumId = string.IsNullOrWhiteSpace(request.Album)
                ? null
                : (await GetOrCreateAlbumAsync(request.Album, track.ArtistId, request.Year ?? track.Year, ct)).Id;
        }

        if (request.Genre is not null)
        {
            track.GenreId = string.IsNullOrWhiteSpace(request.Genre)
                ? null
                : (await GetOrCreateGenreAsync(request.Genre, ct)).Id;
        }

        if (request.Year is not null) track.Year = request.Year;
        if (request.TrackNumber is not null) track.TrackNumber = request.TrackNumber;
        if (request.DiscNumber is not null) track.DiscNumber = request.DiscNumber;

        await db.SaveChangesAsync(ct);
        await CleanUpOrphansAsync(ct);

        logger.LogInformation("Track {TrackId} metadata updated", id);
        return await GetTrackAsync(id, ct);
    }

    public async Task DeleteTrackAsync(Guid id, CancellationToken ct = default)
    {
        var track = await db.Tracks.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("Track not found.");

        var filePath = track.FilePath;
        db.Tracks.Remove(track);
        await db.SaveChangesAsync(ct);


        storage.Delete(filePath);
        await CleanUpOrphansAsync(ct);

        logger.LogInformation("Track {TrackId} deleted along with {FilePath}", id, filePath);
    }

    public async Task<IReadOnlyList<Artist>> ResolveArtistsAsync(
        IEnumerable<string?> rawValues, CancellationToken ct = default)
    {
        var resolved = new List<Artist>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in rawValues)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            foreach (var name in await SplitAgainstLibraryAsync(raw.Trim(), ct))
            {
                if (!seen.Add(Normalize.Key(name)))
                    continue;

                resolved.Add(await GetOrCreateArtistAsync(name, ct));
                if (resolved.Count == ArtistNames.MaxCredits)
                    return resolved;
            }
        }

        return resolved.Count > 0 ? resolved : [await GetOrCreateArtistAsync("Unknown Artist", ct)];
    }

    private async Task<IReadOnlyList<string>> SplitAgainstLibraryAsync(string raw, CancellationToken ct)
    {
        var key = Normalize.Key(raw);
        var known = await db.Artists.AnyAsync(a => a.NormalizedName == key, ct);

        return known ? [raw] : ArtistNames.Split(raw);
    }


    public async Task SetTrackArtistsAsync(Track track, IReadOnlyList<Artist> artists, CancellationToken ct = default)
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

    public async Task<Artist> GetOrCreateArtistAsync(string name, CancellationToken ct = default)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? "Unknown Artist" : name.Trim();
        var key = Normalize.Key(trimmed);

        var existing = await db.Artists.FirstOrDefaultAsync(a => a.NormalizedName == key, ct);
        if (existing is not null)
            return existing;

        var artist = new Artist { Name = trimmed, NormalizedName = key };
        db.Artists.Add(artist);
        return artist;
    }

    public async Task<Album> GetOrCreateAlbumAsync(string title, Guid artistId, int? year, CancellationToken ct = default)
    {
        var trimmed = string.IsNullOrWhiteSpace(title) ? "Unknown Album" : title.Trim();
        var key = Normalize.Key(trimmed);

        var existing = await db.Albums
            .FirstOrDefaultAsync(a => a.NormalizedTitle == key && a.ArtistId == artistId, ct);

        if (existing is not null)
        {
            existing.Year ??= year;
            return existing;
        }

        var album = new Album { Title = trimmed, NormalizedTitle = key, ArtistId = artistId, Year = year };
        db.Albums.Add(album);
        return album;
    }

    public async Task<Genre> GetOrCreateGenreAsync(string name, CancellationToken ct = default)
    {
        var trimmed = name.Trim();
        var key = Normalize.Key(trimmed);

        var existing = await db.Genres.FirstOrDefaultAsync(g => g.NormalizedName == key, ct);
        if (existing is not null)
            return existing;

        var genre = new Genre { Name = trimmed, NormalizedName = key };
        db.Genres.Add(genre);
        return genre;
    }

    public async Task CleanUpOrphansAsync(CancellationToken ct = default)
    {
        var emptyAlbums = await db.Albums.Where(a => !a.Tracks.Any()).ToListAsync(ct);
        foreach (var album in emptyAlbums)
        {
            if (album.CoverPath is not null)
                storage.Delete(album.CoverPath);
            db.Albums.Remove(album);
        }

        if (emptyAlbums.Count > 0)
            await db.SaveChangesAsync(ct);

        var emptyArtists = await db.Artists
            .Where(a => !a.Tracks.Any() && !a.Albums.Any() && !a.TrackCredits.Any())
            .ToListAsync(ct);

        foreach (var artist in emptyArtists.Where(a => a.ImagePath is not null))
            storage.Delete(artist.ImagePath!);

        db.Artists.RemoveRange(emptyArtists);

        var emptyGenres = await db.Genres.Where(g => !g.Tracks.Any()).ToListAsync(ct);
        db.Genres.RemoveRange(emptyGenres);

        if (emptyArtists.Count > 0 || emptyGenres.Count > 0)
            await db.SaveChangesAsync(ct);
    }
}
