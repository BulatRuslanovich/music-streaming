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
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var track = await db.Tracks.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("Track not found.");

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

        await transaction.CommitAsync(ct);

        logger.LogInformation("Track {TrackId} metadata updated", id);
        return await catalog.GetTrackAsync(id, ct);
    }

    public const int MaxBulkDelete = 200;

    public async Task DeleteTrackAsync(Guid id, CancellationToken ct = default)
    {
        if ((await DeleteTracksAsync([id], ct)).Deleted == 0)
            throw new NotFoundException("Track not found.");
    }

    public async Task<BulkDeleteResultDto> DeleteTracksAsync(
        IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        var wanted = ids.Distinct().ToList();

        if (wanted.Count == 0)
            throw new ValidationException("No tracks were selected.");

        if (wanted.Count > MaxBulkDelete)
            throw new ValidationException($"At most {MaxBulkDelete} tracks can be deleted at once.");

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
    }

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

    private async Task CleanUpOrphansAsync(OrphanCandidates touched, CancellationToken ct)
    {
        var candidateArtists = touched.ArtistIds;

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

    private readonly record struct OrphanCandidates(
        List<Guid> AlbumIds,
        List<Guid> ArtistIds,
        List<Guid> GenreIds);

    private readonly record struct TrackFacts(
        Guid Id, Guid ArtistId, Guid? AlbumId, Guid? GenreId, string FilePath, string ContentHash);

    private static TrackFacts FactsOf(Track track) =>
        new(track.Id, track.ArtistId, track.AlbumId, track.GenreId, track.FilePath, track.ContentHash);
}
