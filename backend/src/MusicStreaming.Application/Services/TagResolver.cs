using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Domain.Common;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

/// <summary>
/// Сводит теги файла к сущностям библиотеки, заводя недостающие.
///
/// <para>
/// Помнит всё, что уже свёл, в пределах своего запроса — он зарегистрирован <c>Scoped</c>, а пакет
/// загрузки целиком укладывается в один запрос. Память здесь не только ради скорости. Поиск идёт
/// по базе и не видит того, что этот же запрос уже завёл, но ещё не сохранил, — а один файл
/// спрашивает про исполнителей дважды, отдельно за трек и за альбом. Названный в обоих местах
/// новый исполнитель без памяти заводился бы дважды и упирался в уникальный индекс на
/// <c>normalized_name</c>; раньше от этого спасало сохранение между двумя вызовами.
/// </para>
///
/// <para>
/// Заодно снимается и повтор запросов: пакет из двух сотен файлов одного альбома спрашивал про
/// своего исполнителя по два запроса на файл, а теперь — по два на весь пакет.
/// </para>
/// </summary>
public class TagResolver(IApplicationDbContext db)
{
    private readonly Dictionary<string, Artist> _artists = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Genre> _genres = new(StringComparer.Ordinal);

    /// <summary>Альбомы различаются парой «исполнитель + название» — ровно тем, что объявлено уникальным в схеме.</summary>
    private readonly Dictionary<(Guid ArtistId, string Key), Album> _albums = [];

    /// <summary>Ответы «есть ли в библиотеке исполнитель ровно с таким именем» — см. <see cref="SplitAgainstLibraryAsync"/>.</summary>
    private readonly Dictionary<string, bool> _knownNames = new(StringComparer.Ordinal);

    /// <summary>
    /// Забывает всё сведённое. Зовётся, когда вызывающий отказался от незаписанных изменений:
    /// запомненные сущности после этого отцеплены от контекста, и вернуть такую в ответ на
    /// следующий файл значило бы сослаться на строку, которой никогда не будет.
    /// </summary>
    public void Forget()
    {
        _artists.Clear();
        _albums.Clear();
        _genres.Clear();
        _knownNames.Clear();
    }

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

        if (_albums.TryGetValue((artistId, key), out var cached))
        {
            cached.Year ??= year;
            return cached;
        }

        var album = await db.Albums
            .FirstOrDefaultAsync(a => a.NormalizedTitle == key && a.ArtistId == artistId, ct);

        if (album is null)
        {
            album = new Album { Title = trimmed, NormalizedTitle = key, ArtistId = artistId, Year = year };
            db.Albums.Add(album);
        }
        else
        {
            album.Year ??= year;
        }

        _albums[(artistId, key)] = album;
        return album;
    }

    public async Task<Genre> GetOrCreateGenreAsync(string name, CancellationToken ct = default)
    {
        var trimmed = name.Trim();
        var key = Normalize.Key(trimmed);

        if (_genres.TryGetValue(key, out var cached))
            return cached;

        var genre = await db.Genres.FirstOrDefaultAsync(g => g.NormalizedName == key, ct);
        if (genre is null)
        {
            genre = new Genre { Name = trimmed, NormalizedName = key };
            db.Genres.Add(genre);
        }

        _genres[key] = genre;
        return genre;
    }

    /// <summary>
    /// Составное имя разбивается на соавторов, только если такого исполнителя нет в библиотеке
    /// целиком: «AC/DC» и «Simon &amp; Garfunkel» — это по одному имени, а не по два.
    /// </summary>
    private async Task<IReadOnlyList<string>> SplitAgainstLibraryAsync(string raw, CancellationToken ct)
    {
        var key = Normalize.Key(raw);

        // Заведённое в этом же пакете считается известным наравне с тем, что уже лежит в базе.
        if (_artists.ContainsKey(key))
            return [raw];

        if (!_knownNames.TryGetValue(key, out var known))
        {
            known = await db.Artists.AnyAsync(a => a.NormalizedName == key, ct);
            _knownNames[key] = known;
        }

        return known ? [raw] : ArtistNames.Split(raw);
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
            artist = new Artist { Name = trimmed, NormalizedName = key };
            db.Artists.Add(artist);
        }

        _artists[key] = artist;
        return artist;
    }
}
