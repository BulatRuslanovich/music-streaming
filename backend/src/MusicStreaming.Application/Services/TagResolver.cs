using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Domain.Common;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

public class TagResolver(IApplicationDbContext db)
{
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

    public async Task<Album> GetOrCreateAlbumAsync(
        string title, Guid artistId, int? year, CancellationToken ct = default)
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

    private async Task<IReadOnlyList<string>> SplitAgainstLibraryAsync(string raw, CancellationToken ct)
    {
        var key = Normalize.Key(raw);
        var known = await db.Artists.AnyAsync(a => a.NormalizedName == key, ct);

        return known ? [raw] : ArtistNames.Split(raw);
    }

    private async Task<Artist> GetOrCreateArtistAsync(string name, CancellationToken ct)
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
}
