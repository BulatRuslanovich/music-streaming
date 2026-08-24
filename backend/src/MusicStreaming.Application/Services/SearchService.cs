// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Common;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

public class SearchService(IApplicationDbContext db, ICurrentUser currentUser)
{
    public async Task<SearchResultDto> SearchAsync(string? query, int limit = 20, CancellationToken ct = default)
    {
        if (SearchTerm.For(query) is not { } term)
            return new SearchResultDto([], [], [], [], null);

        limit = Math.Clamp(limit, 1, 50);

        var artists = await RankedArtists(term).Take(limit).Select(ToDto.Artist).ToListAsync(ct);
        var albums = await RankedAlbums(term).Take(limit).Select(ToDto.Album).ToListAsync(ct);
        var tracks = await RankedTracks(term).Take(limit).Select(ToDto.Track(currentUser.Id)).ToListAsync(ct);
        var genres = await RankedGenres(term).Take(limit).Select(ToDto.Genre).ToListAsync(ct);

        return new SearchResultDto(artists, albums, tracks, genres, TopOf(term, artists, albums, tracks, genres));
    }

    public async Task<PagedResult<ArtistDto>> SearchArtistsAsync(
        string? query, PageRequest page, CancellationToken ct = default) =>
        SearchTerm.For(query) is not { } term
            ? PagedResult<ArtistDto>.Empty(page)
            : await RankedArtists(term).ToPagedAsync(page, ToDto.Artist, ct);

    public async Task<PagedResult<AlbumDto>> SearchAlbumsAsync(
        string? query, PageRequest page, CancellationToken ct = default) =>
        SearchTerm.For(query) is not { } term
            ? PagedResult<AlbumDto>.Empty(page)
            : await RankedAlbums(term).ToPagedAsync(page, ToDto.Album, ct);

    public async Task<PagedResult<TrackDto>> SearchTracksAsync(
        string? query, PageRequest page, CancellationToken ct = default) =>
        SearchTerm.For(query) is not { } term
            ? PagedResult<TrackDto>.Empty(page)
            : await RankedTracks(term).ToPagedAsync(page, ToDto.Track(currentUser.Id), ct);

    public async Task<PagedResult<GenreDto>> SearchGenresAsync(
        string? query, PageRequest page, CancellationToken ct = default) =>
        SearchTerm.For(query) is not { } term
            ? PagedResult<GenreDto>.Empty(page)
            : await RankedGenres(term).ToPagedAsync(page, ToDto.Genre, ct);

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

    private IQueryable<Artist> RankedArtists(SearchTerm term)
    {
        var (value, pattern) = term;

        return db.Artists.AsNoTracking()
            .Where(a => EF.Functions.Like(a.NormalizedName, pattern, SearchTerm.EscapeChar))
            .OrderBy(a => SearchRank.Of(a.NormalizedName, value))
            .ThenByDescending(a => a.TrackCredits.Sum(
                credit => credit.Track!.Stats == null ? 0 : credit.Track.Stats.PlayCount))
            .ThenBy(a => a.Name);
    }

    private IQueryable<Album> RankedAlbums(SearchTerm term)
    {
        var (value, pattern) = term;

        return db.Albums.AsNoTracking()
            .Where(a => EF.Functions.Like(a.NormalizedTitle, pattern, SearchTerm.EscapeChar)
                        || EF.Functions.Like(a.Artist!.NormalizedName, pattern, SearchTerm.EscapeChar))
            .OrderBy(a => SearchRank.Of(a.NormalizedTitle, value))
            .ThenByDescending(a => a.Tracks.Sum(t => t.Stats == null ? 0 : t.Stats.PlayCount))
            .ThenBy(a => a.Title);
    }

    private IQueryable<Track> RankedTracks(SearchTerm term)
    {
        var (value, pattern) = term;

        return db.Tracks.AsNoTracking()
            .Where(t => EF.Functions.Like(t.NormalizedTitle, pattern, SearchTerm.EscapeChar)
                        || t.TrackArtists.Any(ta => EF.Functions.Like(ta.Artist!.NormalizedName, pattern, SearchTerm.EscapeChar))
                        || (t.Album != null && EF.Functions.Like(t.Album.NormalizedTitle, pattern, SearchTerm.EscapeChar))
                        || (t.Genre != null && EF.Functions.Like(t.Genre.NormalizedName, pattern, SearchTerm.EscapeChar)))
            .OrderBy(t => SearchRank.Of(t.NormalizedTitle, value))
            .ThenByDescending(t => t.Stats == null ? 0 : t.Stats.PopularityScore)
            .ThenBy(t => t.Title);
    }

    private IQueryable<Genre> RankedGenres(SearchTerm term)
    {
        var (value, pattern) = term;

        return db.Genres.AsNoTracking()
            .Where(g => EF.Functions.Like(g.NormalizedName, pattern, SearchTerm.EscapeChar))
            .OrderBy(g => SearchRank.Of(g.NormalizedName, value))
            .ThenByDescending(g => g.Tracks.Count)
            .ThenBy(g => g.Name);
    }
}
