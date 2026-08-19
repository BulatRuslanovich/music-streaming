// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

public class CatalogService(IApplicationDbContext db, ICurrentUser currentUser, IMemoryCache memoryCache)
{
    public enum TrackSort { Title, Recent, Artist, Album }

    private static readonly TimeSpan LibraryStatsLifetime = TimeSpan.FromMinutes(1);

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

        var playedTracks = await db.TracksByIdAsync(userId, lastPlays.Select(x => x.TrackId), ct);

        var recentlyPlayed = lastPlays
            .Where(x => playedTracks.ContainsKey(x.TrackId))
            .Select(x => playedTracks[x.TrackId])
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

        return new HomeSummaryDto(
            recentlyAdded, recentlyPlayed, favorites, albums, playlists, await LibraryStatsAsync(userId, ct));
    }

    private Task<LibraryStatsDto> LibraryStatsAsync(Guid userId, CancellationToken ct) =>
        memoryCache.GetOrCreateAsync(
            $"library-stats:{userId}",
            entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = LibraryStatsLifetime;
                return QueryLibraryStatsAsync(userId, ct);
            })!;

    private async Task<LibraryStatsDto> QueryLibraryStatsAsync(Guid userId, CancellationToken ct)
    {
        var rows = await db.Set<LibraryStatsRow>().FromSql(
            $"""
            SELECT (SELECT COUNT(*) FROM tracks)::int                             AS tracks,
                   (SELECT COUNT(*) FROM albums)::int                             AS albums,
                   (SELECT COUNT(*) FROM artists)::int                            AS artists,
                   (SELECT COUNT(*) FROM playlists WHERE user_id = {userId})::int AS playlists,
                   (SELECT COALESCE(SUM(duration_seconds), 0) FROM tracks)::bigint AS duration_seconds,
                   (SELECT COALESCE(SUM(file_size), 0) FROM tracks)::bigint        AS total_bytes
            """).ToListAsync(ct);

        var row = rows[0];

        return new LibraryStatsDto(
            row.Tracks, row.Albums, row.Artists, row.Playlists, row.DurationSeconds, row.TotalBytes);
    }
}

public class LibraryStatsRow
{
    public int Tracks { get; set; }
    public int Albums { get; set; }
    public int Artists { get; set; }
    public int Playlists { get; set; }
    public long DurationSeconds { get; set; }
    public long TotalBytes { get; set; }
}
