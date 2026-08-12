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

        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            track.Title = request.Title.Trim();
            track.NormalizedTitle = Normalize.Key(track.Title);
        }

        if (!string.IsNullOrWhiteSpace(request.Artist))
        {
            var artists = await tags.ResolveArtistsAsync([request.Artist], ct);
            track.ArtistId = artists[0].Id;
            await db.SaveChangesAsync(ct); // any new artist needs its id before it is credited
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
        await CleanUpOrphansAsync(ct);

        logger.LogInformation("Track {TrackId} metadata updated", id);
        return await catalog.GetTrackAsync(id, ct);
    }

    public async Task DeleteTrackAsync(Guid id, CancellationToken ct = default)
    {
        var track = await db.Tracks.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("Track not found.");

        var filePath = track.FilePath;
        var contentHash = track.ContentHash;

        db.Tracks.Remove(track);
        await db.SaveChangesAsync(ct);

        storage.Delete(filePath);
        storage.Delete(storage.TranscodePathFor(contentHash));
        await CleanUpOrphansAsync(ct);

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

    private async Task CleanUpOrphansAsync(CancellationToken ct)
    {
        var emptyAlbums = await db.Albums.Where(a => !a.Tracks.Any()).ToListAsync(ct);
        foreach (var album in emptyAlbums)
        {
            if (album.CoverPath is not null)
                storage.DeleteCover(album.CoverPath);
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

        if (emptyArtists.Count > 0)
            await db.SaveChangesAsync(ct);

        await db.Genres.Where(g => !g.Tracks.Any()).ExecuteDeleteAsync(ct);
    }
}
