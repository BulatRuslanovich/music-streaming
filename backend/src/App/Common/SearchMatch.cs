// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Domain.Entities;

namespace App.Common;

public static class SearchMatch
{
    public static IQueryable<Track> Matching(this IQueryable<Track> tracks, SearchTerm? term) =>
        term is not { Pattern: var p }
            ? tracks
            : tracks.Where(t =>
                EF.Functions.Like(t.NormalizedTitle, p, SearchTerm.EscapeChar)
                || t.TrackArtists.Any(ta => EF.Functions.Like(ta.Artist!.NormalizedName, p, SearchTerm.EscapeChar))
                || (t.Album != null && EF.Functions.Like(t.Album.NormalizedTitle, p, SearchTerm.EscapeChar))
                || (t.Genre != null && EF.Functions.Like(t.Genre.NormalizedName, p, SearchTerm.EscapeChar)));

    public static IQueryable<Album> Matching(this IQueryable<Album> albums, SearchTerm? term) =>
        term is not { Pattern: var p }
            ? albums
            : albums.Where(a =>
                EF.Functions.Like(a.NormalizedTitle, p, SearchTerm.EscapeChar)
                || EF.Functions.Like(a.Artist!.NormalizedName, p, SearchTerm.EscapeChar));

    public static IQueryable<Artist> Matching(this IQueryable<Artist> artists, SearchTerm? term) =>
        term is not { Pattern: var p }
            ? artists
            : artists.Where(a => EF.Functions.Like(a.NormalizedName, p, SearchTerm.EscapeChar));
}
