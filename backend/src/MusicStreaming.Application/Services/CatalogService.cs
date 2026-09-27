// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

public class CatalogService(IApplicationDbContext db, ICurrentUser currentUser)
{
    public enum TrackSort { Title, Recent, Artist, Album }

    public const int MaxShuffleTracks = 200;

    public async Task<PagedResult<TrackDto>> GetTracksAsync(
        PageRequest page,
        TrackSort sort,
        string? search,
        CancellationToken ct)
    {
        var query = FilterTracks(search);

        var ordered = sort switch
        {
            TrackSort.Recent => query.OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Title),
            TrackSort.Artist => query.OrderBy(t => t.Artist!.Name).ThenBy(t => t.Title),
            TrackSort.Album => query.OrderBy(t => t.Album!.Title).ThenBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber),
            _ => query.OrderBy(t => t.Title).ThenBy(t => t.Artist!.Name),
        };

        return await ordered.ToPagedAsync(page, ToDto.Track(currentUser.Id), ct);
    }

    public async Task<IReadOnlyList<TrackDto>> GetShuffledTracksAsync(int? limit, string? search, CancellationToken ct)
    {
        var take = limit is null or < 1 ? MaxShuffleTracks : Math.Min(limit.Value, MaxShuffleTracks);
        var pivot = Random.Shared.NextDouble();
        var query = FilterTracks(search);

        var selected = await query
            .Where(track => track.ShuffleKey >= pivot)
            .OrderBy(track => track.ShuffleKey)
            .ThenBy(track => track.Id)
            .Take(take)
            .Select(ToDto.Track(currentUser.Id))
            .ToListAsync(ct);

        if (selected.Count < take)
        {
            selected.AddRange(await query
                .Where(track => track.ShuffleKey < pivot)
                .OrderBy(track => track.ShuffleKey)
                .ThenBy(track => track.Id)
                .Take(take - selected.Count)
                .Select(ToDto.Track(currentUser.Id))
                .ToListAsync(ct));
        }

        for (var index = selected.Count - 1; index > 0; index--)
        {
            var swap = Random.Shared.Next(index + 1);
            (selected[index], selected[swap]) = (selected[swap], selected[index]);
        }

        return selected;
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

    public async Task<TrackDto> GetTrackAsync(Guid id, CancellationToken ct)
    {
        var track = await db.Tracks.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(ToDto.Track(currentUser.Id))
            .FirstOrDefaultAsync(ct);

        return track ?? throw new NotFoundException("Track not found.");
    }

    /// <summary>
    /// Разбор записи. Неудачный анализ равен отсутствующему: наружу отдавать нули, которые
    /// ничего не измеряют, хуже, чем честный 404.
    /// </summary>
    public async Task<TrackAnalysisDto> GetTrackAnalysisAsync(Guid id, CancellationToken ct)
    {
        var analysis = await db.TrackAudioFeatures.AsNoTracking()
            .Where(f => f.TrackId == id && f.Succeeded)
            .Select(ToDto.TrackAnalysis)
            .FirstOrDefaultAsync(ct);

        return analysis ?? throw new NotFoundException("Track analysis not found.");
    }

    public async Task<PagedResult<ArtistDto>> GetArtistsAsync(PageRequest page, string? search, CancellationToken ct)
    {
        var query = db.Artists.AsNoTracking();

        if (SearchTerm.For(search) is { Pattern: var pattern })
            query = query.Where(a => EF.Functions.Like(a.NormalizedName, pattern, SearchTerm.EscapeChar));

        return await query.OrderBy(a => a.Name).ToPagedAsync(page, ToDto.Artist, ct);
    }

    public async Task<ArtistDetailDto> GetArtistAsync(Guid id, PageRequest? trackPage, CancellationToken ct)
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
            .Select(ToDto.Album)
            .ToListAsync(ct);

        var tracks = await db.Tracks.AsNoTracking()
            .Where(t => t.TrackArtists.Any(ta => ta.ArtistId == id))
            .OrderBy(t => t.Title)
            .ToPagedAsync(page, ToDto.Track(currentUser.Id), ct);

        return new ArtistDetailDto(artist.Id, artist.Name, artist.ImagePath != null, albums, tracks);
    }

    public async Task<IReadOnlyList<TrackDto>> GetArtistTopTracksAsync(
        Guid id, int limit, CancellationToken ct)
    {
        await db.RequireArtistAsync(id, ct);

        return await db.Tracks.AsNoTracking()
            .Where(t => t.TrackArtists.Any(ta => ta.ArtistId == id))
            .OrderByDescending(TrackQueries.Popularity)
            .ThenByDescending(TrackQueries.Plays)
            .ThenBy(t => t.Title)
            .Take(limit)
            .Select(ToDto.Track(currentUser.Id))
            .ToListAsync(ct);
    }

    public async Task<PagedResult<AlbumDto>> GetAlbumsAsync(
        PageRequest page,
        Guid? artistId,
        bool filterByRecent,
        string? search,
        CancellationToken ct)
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

        var ordered = filterByRecent
            ? query.OrderByDescending(a => a.CreatedAt)
            : query.OrderBy(a => a.Title);

        return await ordered.ToPagedAsync(page, ToDto.Album, ct);
    }

    public async Task<AlbumDetailDto> GetAlbumAsync(Guid id, CancellationToken ct)
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
            .Select(ToDto.Track(currentUser.Id))
            .ToListAsync(ct);

        return new AlbumDetailDto(
            album.Id, album.Title, album.ArtistId, album.ArtistName,
            album.Year, album.HasCover, album.Duration, tracks);
    }

    private const int GenreCoverCount = 4;

    public async Task<IReadOnlyList<GenreDto>> GetGenresAsync(CancellationToken ct)
    {
        var genres = await db.Genres.AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(ToDto.Genre)
            .ToListAsync(ct);

        var covers = await GenreCoversAsync(ct);

        return [.. genres.Select(g =>
            covers.TryGetValue(g.Id, out var albumIds) ? g with { CoverAlbumIds = albumIds } : g)];
    }

    private async Task<Dictionary<Guid, IReadOnlyList<Guid>>> GenreCoversAsync(CancellationToken ct)
    {
        var rows = await db.Set<GenreCoverRow>().FromSql(
            $"""
            SELECT genre_id, album_id
            FROM (
                SELECT genre_id, album_id,
                       row_number() OVER (PARTITION BY genre_id ORDER BY album_id) AS row_num
                FROM (
                    SELECT DISTINCT t.genre_id AS genre_id, t.album_id AS album_id
                    FROM tracks t
                    JOIN albums a ON a.id = t.album_id
                    WHERE t.genre_id IS NOT NULL AND a.cover_path IS NOT NULL
                ) pairs
            ) ranked
            WHERE row_num <= {GenreCoverCount}
            """).ToListAsync(ct);

        return rows
            .GroupBy(r => r.GenreId)
            .ToDictionary(g => g.Key, IReadOnlyList<Guid> (g) => [.. g.Select(r => r.AlbumId)]);
    }

    public async Task<PagedResult<TrackDto>> GetGenreTracksAsync(
        Guid genreId, PageRequest page, CancellationToken ct)
    {
        await db.RequireGenreAsync(genreId, ct);

        return await db.Tracks.AsNoTracking()
            .Where(t => t.GenreId == genreId)
            .OrderBy(t => t.Artist!.Name).ThenBy(t => t.Title)
            .ToPagedAsync(page, ToDto.Track(currentUser.Id), ct);
    }
}

public class GenreCoverRow
{
    public Guid GenreId { get; set; }
    public Guid AlbumId { get; set; }
}
