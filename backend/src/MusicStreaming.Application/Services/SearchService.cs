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

        // Четыре независимые выборки на один ввод. Последовательно, одна за другой, на канале
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

        // Лучший результат — сильнейшее совпадение среди первых мест каждого типа; при равенстве
        // побеждает тип, идущий раньше: артист, альбом, трек, жанр.
        (string? Name, SearchTopResultDto Result)[] leaders =
        [
            (artists.FirstOrDefault()?.Name, new SearchTopResultDto(SearchResultKind.Artist, artists.FirstOrDefault(), null, null, null)),
            (albums.FirstOrDefault()?.Title, new SearchTopResultDto(SearchResultKind.Album, null, albums.FirstOrDefault(), null, null)),
            (tracks.FirstOrDefault()?.Title, new SearchTopResultDto(SearchResultKind.Track, null, null, tracks.FirstOrDefault(), null)),
            (genres.FirstOrDefault()?.Name, new SearchTopResultDto(SearchResultKind.Genre, null, null, null, genres.FirstOrDefault())),
        ];

        var top = leaders
            .Where(leader => leader.Name is not null)
            .OrderBy(leader => SearchRank.Evaluate(Normalize.Key(leader.Name!), term.Value))
            .Select(leader => leader.Result)
            .FirstOrDefault();

        return new SearchResultDto(artists, albums, tracks, genres, top);
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

    private static IQueryable<Artist> RankedArtists(IApplicationDbContext db, SearchTerm term) =>
        db.Artists.AsNoTracking().Matching(term)
            .OrderBy(a => SearchRank.Of(a.NormalizedName, term.Value))
            .ThenByDescending(a => a.TrackCredits.Sum(
                credit => credit.Track!.Stats == null ? 0 : credit.Track.Stats.PlayCount))
            .ThenBy(a => a.Name);

    private static IQueryable<Album> RankedAlbums(IApplicationDbContext db, SearchTerm term) =>
        db.Albums.AsNoTracking().Matching(term)
            .OrderBy(a => SearchRank.Of(a.NormalizedTitle, term.Value))
            .ThenByDescending(a => a.Tracks.Sum(t => t.Stats == null ? 0 : t.Stats.PlayCount))
            .ThenBy(a => a.Title);

    private static IQueryable<Track> RankedTracks(IApplicationDbContext db, SearchTerm term) =>
        db.Tracks.AsNoTracking().Matching(term)
            .OrderBy(t => SearchRank.Of(t.NormalizedTitle, term.Value))
            .ThenByDescending(TrackQueries.Popularity)
            .ThenBy(t => t.Title);

    private static IQueryable<Genre> RankedGenres(IApplicationDbContext db, SearchTerm term) =>
        db.Genres.AsNoTracking()
            .Where(g => EF.Functions.Like(g.NormalizedName, term.Pattern, SearchTerm.EscapeChar))
            .OrderBy(g => SearchRank.Of(g.NormalizedName, term.Value))
            .ThenByDescending(g => g.Tracks.Count)
            .ThenBy(g => g.Name);
}
