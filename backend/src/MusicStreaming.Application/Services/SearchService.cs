using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Services;

public class SearchService(IApplicationDbContext db, ICurrentUser currentUser)
{
    public async Task<SearchResultDto> SearchAsync(string? query, int limit = 20, CancellationToken ct = default)
    {
        var term = Normalize.Key(query ?? string.Empty);
        if (term.Length == 0)
            return new SearchResultDto([], [], [], []);

        limit = Math.Clamp(limit, 1, 50);
        var pattern = $"%{Escape(term)}%";

        var artists = await db.Artists.AsNoTracking()
            .Where(a => EF.Functions.Like(a.NormalizedName, pattern, EscapeChar))
            .OrderBy(a => a.Name)
            .Take(limit)
            .Select(Projections.Artist)
            .ToListAsync(ct);

        var albums = await db.Albums.AsNoTracking()
            .Where(a => EF.Functions.Like(a.NormalizedTitle, pattern, EscapeChar)
                        || EF.Functions.Like(a.Artist!.NormalizedName, pattern, EscapeChar))
            .OrderBy(a => a.Title)
            .Take(limit)
            .Select(Projections.Album)
            .ToListAsync(ct);

        var tracks = await db.Tracks.AsNoTracking()
            .Where(t => EF.Functions.Like(t.NormalizedTitle, pattern, EscapeChar)
                        // Matches on any credited artist, not just the primary one.
                        || t.TrackArtists.Any(ta => EF.Functions.Like(ta.Artist!.NormalizedName, pattern, EscapeChar))
                        || (t.Album != null && EF.Functions.Like(t.Album.NormalizedTitle, pattern, EscapeChar))
                        || (t.Genre != null && EF.Functions.Like(t.Genre.NormalizedName, pattern, EscapeChar)))
            .OrderBy(t => t.Title)
            .Take(limit)
            .Select(Projections.Track(currentUser.Id))
            .ToListAsync(ct);

        var genres = await db.Genres.AsNoTracking()
            .Where(g => EF.Functions.Like(g.NormalizedName, pattern, EscapeChar))
            .OrderBy(g => g.Name)
            .Take(limit)
            .Select(Projections.Genre)
            .ToListAsync(ct);

        return new SearchResultDto(artists, albums, tracks, genres);
    }

    private const string EscapeChar = "\\";

    private static string Escape(string term) => term
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");
}
