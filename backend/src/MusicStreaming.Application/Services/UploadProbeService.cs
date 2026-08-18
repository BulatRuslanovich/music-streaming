using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Services;

public class UploadProbeService(IApplicationDbContext db, ICurrentUser currentUser)
{
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

    private async Task<Dictionary<int, Guid>> MatchByTagsAsync(
        IReadOnlyList<UploadProbeFileDto> files, Dictionary<int, Guid> alreadyMatched, CancellationToken ct)
    {
        var candidates = new Dictionary<int, (string TitleKey, HashSet<string> ArtistKeys)>();

        for (var index = 0; index < files.Count; index++)
        {
            if (alreadyMatched.ContainsKey(index))
                continue;

            var file = files[index];

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

            var match = sameTitleTracks.Find(t => t.ArtistKeys.Any(candidate.ArtistKeys.Contains));
            if (match is not null)
                matches[index] = match.Id;
        }

        return matches;
    }

    private static string? NormalizeHash(string? value)
    {
        if (value is null || value.Length != HashLength)
            return null;

        var hash = value.ToLowerInvariant();
        return hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') ? hash : null;
    }
}
