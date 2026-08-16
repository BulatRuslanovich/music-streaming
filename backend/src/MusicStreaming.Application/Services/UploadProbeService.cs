using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Services;

/// <summary>
/// Сверяет с библиотекой то, что известно о файле до загрузки.
///
/// Дубликаты отлавливались и раньше, но только после того, как файл целиком пересекал сеть и
/// ложился на диск: хеш считается по мере записи, а сравнивается уже в конце. Здесь та же проверка
/// делается по одному хешу и паре тегов, которые браузер вычитывает у себя, — совпавший файл не
/// отправляется вовсе.
/// </summary>
public class UploadProbeService(IApplicationDbContext db, ICurrentUser currentUser)
{
    /// <summary>Столько файлов разом ещё разумно разложить по очереди загрузки вручную.</summary>
    public const int MaxFiles = 250;

    private const int HashLength = 64;

    public async Task<UploadProbeResultDto> ProbeAsync(
        IReadOnlyList<UploadProbeFileDto> files, CancellationToken ct = default)
    {
        if (files.Count == 0)
            return new UploadProbeResultDto([]);

        if (files.Count > MaxFiles)
            throw new ValidationException($"No more than {MaxFiles} files can be checked at once.");

        var byHash = await MatchByHashAsync(files, ct);
        var byTags = await MatchByTagsAsync(files, byHash, ct);

        var matched = await db.TracksByIdAsync(currentUser.Id, byHash.Values.Concat(byTags.Values), ct);

        var verdicts = new List<UploadProbeMatchDto>(files.Count);
        for (var index = 0; index < files.Count; index++)
        {
            var (verdict, trackId) = byHash.TryGetValue(index, out var exact)
                ? (UploadProbeVerdict.Duplicate, exact)
                : byTags.TryGetValue(index, out var similar)
                    ? (UploadProbeVerdict.Similar, similar)
                    : (UploadProbeVerdict.New, Guid.Empty);

            verdicts.Add(new UploadProbeMatchDto(
                files[index].FileName,
                verdict,
                trackId == Guid.Empty ? null : matched.GetValueOrDefault(trackId)));
        }

        return new UploadProbeResultDto(verdicts);
    }

    /// <summary>Совпадение по хешу — тот же самый файл, поэтому загружать его точно не нужно.</summary>
    private async Task<Dictionary<int, Guid>> MatchByHashAsync(
        IReadOnlyList<UploadProbeFileDto> files, CancellationToken ct)
    {
        var hashes = new Dictionary<int, string>();
        for (var index = 0; index < files.Count; index++)
        {
            if (NormalizeHash(files[index].ContentHash) is { } hash)
                hashes[index] = hash;
        }

        if (hashes.Count == 0)
            return [];

        var distinct = hashes.Values.Distinct().ToList();
        var known = await db.Tracks
            .Where(t => distinct.Contains(t.ContentHash))
            .Select(t => new { t.ContentHash, t.Id })
            .ToDictionaryAsync(t => t.ContentHash, t => t.Id, ct);

        return hashes
            .Where(pair => known.ContainsKey(pair.Value))
            .ToDictionary(pair => pair.Key, pair => known[pair.Value]);
    }

    /// <summary>
    /// Совпадение по тегам ловит ту же песню, переданную другим файлом: перекодированную, с иным
    /// битрейтом или просто перетегированную. Побайтово такие файлы не совпадут никогда, поэтому
    /// одного хеша мало.
    /// </summary>
    private async Task<Dictionary<int, Guid>> MatchByTagsAsync(
        IReadOnlyList<UploadProbeFileDto> files, Dictionary<int, Guid> alreadyMatched, CancellationToken ct)
    {
        var candidates = new Dictionary<int, (string TitleKey, HashSet<string> ArtistKeys)>();

        for (var index = 0; index < files.Count; index++)
        {
            if (alreadyMatched.ContainsKey(index))
                continue;

            var file = files[index];

            // Без обеих половин пары сравнивать нечего: одно название совпадает у слишком многих
            // песен, а одного исполнителя мало и подавно.
            if (Text.TrimToNull(file.Title) is not { } title || Text.TrimToNull(file.Artist) is not { } artist)
                continue;

            var artistKeys = ArtistNames.Split(artist).Select(Normalize.Key).ToHashSet(StringComparer.Ordinal);
            if (artistKeys.Count == 0)
                continue;

            candidates[index] = (Normalize.Key(title), artistKeys);
        }

        if (candidates.Count == 0)
            return [];

        var titleKeys = candidates.Values.Select(c => c.TitleKey).Distinct().ToList();

        var sameTitle = await db.Tracks
            .Where(t => titleKeys.Contains(t.NormalizedTitle))
            .Select(t => new
            {
                t.Id,
                t.NormalizedTitle,
                ArtistKeys = t.TrackArtists.Select(ta => ta.Artist!.NormalizedName).ToList(),
            })
            .ToListAsync(ct);

        var byTitle = sameTitle
            .GroupBy(t => t.NormalizedTitle, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var matches = new Dictionary<int, Guid>();
        foreach (var (index, candidate) in candidates)
        {
            if (!byTitle.TryGetValue(candidate.TitleKey, out var sameTitleTracks))
                continue;

            // Достаточно одного общего исполнителя: у трека их может быть несколько, а тег файла
            // способен назвать лишь часть состава.
            var match = sameTitleTracks.Find(t => t.ArtistKeys.Any(candidate.ArtistKeys.Contains));
            if (match is not null)
                matches[index] = match.Id;
        }

        return matches;
    }

    /// <summary>
    /// Хеш из браузера принимается только в том виде, в каком хранилище пишет свой, — иначе
    /// сравнение молча не найдёт совпадений. Мусор не отвергается: файл просто пройдёт проверку
    /// как новый, а сервер поймает дубликат по-старому, уже после загрузки.
    /// </summary>
    private static string? NormalizeHash(string? value)
    {
        if (value is null || value.Length != HashLength)
            return null;

        var hash = value.ToLowerInvariant();
        return hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') ? hash : null;
    }
}
