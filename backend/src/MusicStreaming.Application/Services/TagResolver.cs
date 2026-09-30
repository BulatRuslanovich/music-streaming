// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Domain.Common;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

public class TagResolver(IApplicationDbContext db, TimeProvider clock)
{
    private readonly Dictionary<string, Artist> _artists = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Genre> _genres = new(StringComparer.Ordinal);
    private readonly Dictionary<(Guid ArtistId, string Key), Album> _albums = [];
    private readonly Dictionary<string, bool> _knownNames = new(StringComparer.Ordinal);

    public void Forget()
    {
        _artists.Clear();
        _albums.Clear();
        _genres.Clear();
        _knownNames.Clear();
    }

    public async Task<IReadOnlyList<Artist>> ResolveArtistsAsync(
        IEnumerable<string?> rawValues, CancellationToken ct)
    {
        var resolved = new List<Artist>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in rawValues)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var whole = raw.Trim();
            var key = Normalize.Key(whole);

            // Имя, которое библиотека уже знает целиком, на соавторов не режется.
            if (!_artists.ContainsKey(key) && !_knownNames.ContainsKey(key))
                _knownNames[key] = await db.Artists.AnyAsync(a => a.NormalizedName == key, ct);

            IReadOnlyList<string> names = _artists.ContainsKey(key) || _knownNames[key] ? [whole] : ArtistNames.Split(whole);

            foreach (var name in names)
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

    public async Task<Album> GetOrCreateAlbumAsync(
        string title, Guid artistId, int? year, CancellationToken ct)
    {
        var trimmed = string.IsNullOrWhiteSpace(title) ? "Unknown Album" : title.Trim();
        var key = Normalize.Key(trimmed);

        if (_albums.TryGetValue((artistId, key), out var cached))
        {
            cached.Year ??= year;
            return cached;
        }

        var album = await db.Albums
            .FirstOrDefaultAsync(a => a.NormalizedTitle == key && a.ArtistId == artistId, ct);

        if (album is null)
        {
            album = new Album
            {
                Title = trimmed,
                NormalizedTitle = key,
                ArtistId = artistId,
                Year = year,
                CreatedAt = clock.GetUtcNow(),
            };
            db.Albums.Add(album);
        }
        else
        {
            album.Year ??= year;
        }

        _albums[(artistId, key)] = album;
        return album;
    }

    public async Task<Genre> GetOrCreateGenreAsync(string name, CancellationToken ct)
    {
        var trimmed = name.Trim();
        var key = Normalize.Key(trimmed);

        if (_genres.TryGetValue(key, out var cached))
            return cached;

        var genre = await db.Genres.FirstOrDefaultAsync(g => g.NormalizedName == key, ct);
        if (genre is null)
        {
            genre = new Genre { Name = trimmed, NormalizedName = key, CreatedAt = clock.GetUtcNow() };
            db.Genres.Add(genre);
        }

        _genres[key] = genre;
        return genre;
    }

    private async Task<Artist> GetOrCreateArtistAsync(string name, CancellationToken ct)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? "Unknown Artist" : name.Trim();
        var key = Normalize.Key(trimmed);

        if (_artists.TryGetValue(key, out var cached))
            return cached;

        var artist = await db.Artists.FirstOrDefaultAsync(a => a.NormalizedName == key, ct);
        if (artist is null)
        {
            artist = new Artist { Name = trimmed, NormalizedName = key, CreatedAt = clock.GetUtcNow() };
            db.Artists.Add(artist);
        }

        _artists[key] = artist;
        return artist;
    }
}
