using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;

namespace MusicStreaming.Application.Services;

/// <summary>
/// Поиск по всей библиотеке.
///
/// <para>
/// Порядок задаёт <see cref="SearchRank"/>: сначала точное совпадение, затем начало названия, затем
/// начало любого слова внутри него, затем вхождение — и лишь потом то, что нашлось по смежному полю.
/// Внутри одного ранга выигрывает то, что в этой библиотеке действительно слушают, а при равной
/// популярности — алфавит, чтобы выдача не переставлялась от запроса к запросу.
/// </para>
///
/// <para>
/// И отбор, и ранжирование, и ограничение считает база. Ранжировать в памяти означало бы сначала
/// вытащить все совпадения — а «а» совпадает почти со всей фонотекой.
/// </para>
/// </summary>
public class SearchService(IApplicationDbContext db, ICurrentUser currentUser)
{
    public async Task<SearchResultDto> SearchAsync(string? query, int limit = 20, CancellationToken ct = default)
    {
        if (SearchTerm.For(query) is not { } term)
            return new SearchResultDto([], [], [], []);

        limit = Math.Clamp(limit, 1, 50);

        var (value, pattern) = term;

        var artists = await db.Artists.AsNoTracking()
            .Where(a => EF.Functions.Like(a.NormalizedName, pattern, SearchTerm.EscapeChar))
            .OrderBy(a => SearchRank.Of(a.NormalizedName, value))
            .ThenByDescending(a => a.TrackCredits.Sum(
                credit => credit.Track!.Stats == null ? 0 : credit.Track.Stats.PlayCount))
            .ThenBy(a => a.Name)
            .Take(limit)
            .Select(Projections.Artist)
            .ToListAsync(ct);

        var albums = await db.Albums.AsNoTracking()
            .Where(a => EF.Functions.Like(a.NormalizedTitle, pattern, SearchTerm.EscapeChar)
                        || EF.Functions.Like(a.Artist!.NormalizedName, pattern, SearchTerm.EscapeChar))
            .OrderBy(a => SearchRank.Of(a.NormalizedTitle, value))
            .ThenByDescending(a => a.Tracks.Sum(t => t.Stats == null ? 0 : t.Stats.PlayCount))
            .ThenBy(a => a.Title)
            .Take(limit)
            .Select(Projections.Album)
            .ToListAsync(ct);

        var tracks = await db.Tracks.AsNoTracking()
            .Where(t => EF.Functions.Like(t.NormalizedTitle, pattern, SearchTerm.EscapeChar)
                        // Совпадает по любому заявленному исполнителю, а не только по основному.
                        || t.TrackArtists.Any(ta => EF.Functions.Like(ta.Artist!.NormalizedName, pattern, SearchTerm.EscapeChar))
                        || (t.Album != null && EF.Functions.Like(t.Album.NormalizedTitle, pattern, SearchTerm.EscapeChar))
                        || (t.Genre != null && EF.Functions.Like(t.Genre.NormalizedName, pattern, SearchTerm.EscapeChar)))
            // Ранг считается по названию: трек, нашедшийся по исполнителю или альбому, получает
            // последний ранг и встаёт после всех, чьё название действительно совпало.
            .OrderBy(t => SearchRank.Of(t.NormalizedTitle, value))
            .ThenByDescending(t => t.Stats == null ? 0 : t.Stats.PopularityScore)
            .ThenBy(t => t.Title)
            .Take(limit)
            .Select(Projections.Track(currentUser.Id))
            .ToListAsync(ct);

        var genres = await db.Genres.AsNoTracking()
            .Where(g => EF.Functions.Like(g.NormalizedName, pattern, SearchTerm.EscapeChar))
            .OrderBy(g => SearchRank.Of(g.NormalizedName, value))
            .ThenByDescending(g => g.Tracks.Count)
            .ThenBy(g => g.Name)
            .Take(limit)
            .Select(Projections.Genre)
            .ToListAsync(ct);

        return new SearchResultDto(artists, albums, tracks, genres);
    }
}
