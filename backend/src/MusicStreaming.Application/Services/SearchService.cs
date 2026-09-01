// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Common;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

public class SearchService(
    IApplicationDbContext db,
    IApplicationDbContextFactory contextFactory,
    ICurrentUser currentUser)
{
    public async Task<SearchResultDto> SearchAsync(string? query, int limit = 20, CancellationToken ct = default)
    {
        if (SearchTerm.ForSearch(query) is not { } term)
            return new SearchResultDto([], [], [], [], null);

        limit = Math.Clamp(limit, 1, 50);

        // Четыре независимые выборки на один ввод — раньше они шли одна за другой, и на канале
        // с высоким пингом задержка складывалась четырежды за каждое нажатие клавиши.
        // Контекст на каждую свой: делить один между параллельными запросами нельзя.
        var artistsQuery = contextFactory.QueryAsync(scoped =>
            RankedArtists(scoped, term).Take(limit).Select(ToDto.Artist).ToListAsync(ct));
        var albumsQuery = contextFactory.QueryAsync(scoped =>
            RankedAlbums(scoped, term).Take(limit).Select(ToDto.Album).ToListAsync(ct));
        var tracksQuery = contextFactory.QueryAsync(scoped =>
            RankedTracks(scoped, term).Take(limit).Select(ToDto.Track(currentUser.Id)).ToListAsync(ct));
        var genresQuery = contextFactory.QueryAsync(scoped =>
            RankedGenres(scoped, term).Take(limit).Select(ToDto.Genre).ToListAsync(ct));

        await Task.WhenAll(artistsQuery, albumsQuery, tracksQuery, genresQuery);

        var artists = await artistsQuery;
        var albums = await albumsQuery;
        var tracks = await tracksQuery;
        var genres = await genresQuery;

        return new SearchResultDto(artists, albums, tracks, genres, TopOf(term, artists, albums, tracks, genres));
    }

    public async Task<PagedResult<ArtistDto>> SearchArtistsAsync(
        string? query, PageRequest page, CancellationToken ct = default) =>
        SearchTerm.ForSearch(query) is not { } term
            ? PagedResult<ArtistDto>.Empty(page)
            : await RankedArtists(db, term).ToPagedAsync(page, ToDto.Artist, ct);

    public async Task<PagedResult<AlbumDto>> SearchAlbumsAsync(
        string? query, PageRequest page, CancellationToken ct = default) =>
        SearchTerm.ForSearch(query) is not { } term
            ? PagedResult<AlbumDto>.Empty(page)
            : await RankedAlbums(db, term).ToPagedAsync(page, ToDto.Album, ct);

    public async Task<PagedResult<TrackDto>> SearchTracksAsync(
        string? query, PageRequest page, CancellationToken ct = default) =>
        SearchTerm.ForSearch(query) is not { } term
            ? PagedResult<TrackDto>.Empty(page)
            : await RankedTracks(db, term).ToPagedAsync(page, ToDto.Track(currentUser.Id), ct);

    public async Task<PagedResult<GenreDto>> SearchGenresAsync(
        string? query, PageRequest page, CancellationToken ct = default) =>
        SearchTerm.ForSearch(query) is not { } term
            ? PagedResult<GenreDto>.Empty(page)
            : await RankedGenres(db, term).ToPagedAsync(page, ToDto.Genre, ct);

    private static SearchTopResultDto? TopOf(
        SearchTerm term,
        IReadOnlyList<ArtistDto> artists,
        IReadOnlyList<AlbumDto> albums,
        IReadOnlyList<TrackDto> tracks,
        IReadOnlyList<GenreDto> genres)
    {
        var candidates = new List<(int Rank, int Tie, SearchTopResultDto Result)>();

        if (artists.Count > 0)
            candidates.Add((Rank(artists[0].Name), 0,
                new SearchTopResultDto(SearchResultKind.Artist, artists[0], null, null, null)));

        if (albums.Count > 0)
            candidates.Add((Rank(albums[0].Title), 1,
                new SearchTopResultDto(SearchResultKind.Album, null, albums[0], null, null)));

        if (tracks.Count > 0)
            candidates.Add((Rank(tracks[0].Title), 2,
                new SearchTopResultDto(SearchResultKind.Track, null, null, tracks[0], null)));

        if (genres.Count > 0)
            candidates.Add((Rank(genres[0].Name), 3,
                new SearchTopResultDto(SearchResultKind.Genre, null, null, null, genres[0])));

        return candidates.Count == 0
            ? null
            : candidates.OrderBy(c => c.Rank).ThenBy(c => c.Tie).First().Result;

        int Rank(string name) => SearchRank.Evaluate(Normalize.Key(name), term.Value);
    }

    private static IQueryable<Artist> RankedArtists(IApplicationDbContext db, SearchTerm term)
    {
        var (value, pattern) = term;

        return db.Artists.AsNoTracking()
            .Where(a => EF.Functions.Like(a.NormalizedName, pattern, SearchTerm.EscapeChar))
            .OrderBy(a => SearchRank.Of(a.NormalizedName, value))
            .ThenByDescending(a => a.TrackCredits.Sum(
                credit => credit.Track!.Stats == null ? 0 : credit.Track.Stats.PlayCount))
            .ThenBy(a => a.Name);
    }

    private static IQueryable<Album> RankedAlbums(IApplicationDbContext db, SearchTerm term)
    {
        var (value, pattern) = term;

        return db.Albums.AsNoTracking()
            .Where(a => EF.Functions.Like(a.NormalizedTitle, pattern, SearchTerm.EscapeChar)
                        || EF.Functions.Like(a.Artist!.NormalizedName, pattern, SearchTerm.EscapeChar))
            .OrderBy(a => SearchRank.Of(a.NormalizedTitle, value))
            .ThenByDescending(a => a.Tracks.Sum(t => t.Stats == null ? 0 : t.Stats.PlayCount))
            .ThenBy(a => a.Title);
    }

    private static IQueryable<Track> RankedTracks(IApplicationDbContext db, SearchTerm term)
    {
        var (value, pattern) = term;

        return db.Tracks.AsNoTracking()
            .Where(t => EF.Functions.Like(t.NormalizedTitle, pattern, SearchTerm.EscapeChar)
                        || t.TrackArtists.Any(ta => EF.Functions.Like(ta.Artist!.NormalizedName, pattern, SearchTerm.EscapeChar))
                        || (t.Album != null && EF.Functions.Like(t.Album.NormalizedTitle, pattern, SearchTerm.EscapeChar))
                        || (t.Genre != null && EF.Functions.Like(t.Genre.NormalizedName, pattern, SearchTerm.EscapeChar)))
            .OrderBy(t => SearchRank.Of(t.NormalizedTitle, value))
            .ThenByDescending(TrackQueries.Popularity)
            .ThenBy(t => t.Title);
    }

    private static IQueryable<Genre> RankedGenres(IApplicationDbContext db, SearchTerm term)
    {
        var (value, pattern) = term;

        return db.Genres.AsNoTracking()
            .Where(g => EF.Functions.Like(g.NormalizedName, pattern, SearchTerm.EscapeChar))
            .OrderBy(g => SearchRank.Of(g.NormalizedName, value))
            .ThenByDescending(g => g.Tracks.Count)
            .ThenBy(g => g.Name);
    }
}
