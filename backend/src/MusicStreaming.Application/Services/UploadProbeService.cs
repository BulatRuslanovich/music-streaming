// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

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

    private sealed record TagKeys(string TitleKey, HashSet<string> ArtistKeys);

    public async Task<UploadProbeResultDto> ProbeAsync(IReadOnlyList<UploadProbeFileDto> files, CancellationToken ct)
    {
        if (files.Count == 0)
            return new UploadProbeResultDto([]);

        if (files.Count > MaxFiles)
            throw new ValidationException($"No more than {MaxFiles} files can be checked at once.");

        var hashes = new Dictionary<int, string>();
        var candidates = new Dictionary<int, TagKeys>();

        foreach (var (index, file) in files.Index())
        {
            var hash = file.ContentHash?.ToLowerInvariant();
            if (hash is { Length: HashLength } && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
                hashes[index] = hash;

            if (Text.TrimToNull(file.Title) is { } title && Text.TrimToNull(file.Artist) is { } artist)
            {
                var artistKeys = ArtistNames.Split(artist).Select(Normalize.Key).ToHashSet(StringComparer.Ordinal);
                if (artistKeys.Count > 0)
                    candidates[index] = new TagKeys(Normalize.Key(title), artistKeys);
            }
        }

        var distinctHashes = hashes.Values.Distinct().ToList();
        Dictionary<string, Guid> known = distinctHashes.Count == 0
            ? []
            : await db.Tracks
                .Where(t => distinctHashes.Contains(t.ContentHash))
                .ToDictionaryAsync(t => t.ContentHash, t => t.Id, ct);

        var byHash = hashes
            .Where(pair => known.ContainsKey(pair.Value))
            .ToDictionary(pair => pair.Key, pair => known[pair.Value]);

        var byTags = await MatchByTagsAsync(candidates.Where(pair => !byHash.ContainsKey(pair.Key)).ToDictionary(), ct);

        var matched = await db.TracksByIdAsync(currentUser.Id, byHash.Values.Concat(byTags.Values), ct);

        var verdicts = new List<UploadProbeMatchDto>(files.Count);
        for (var index = 0; index < files.Count; index++)
        {
            var (verdict, trackId) = byHash.TryGetValue(index, out var exact)
                ? (UploadProbeVerdict.Duplicate, exact)
                : byTags.TryGetValue(index, out var similar)
                    ? (UploadProbeVerdict.Similar, similar)
                    : (UploadProbeVerdict.New, Guid.Empty);

            var basis = (hashes.ContainsKey(index), candidates.ContainsKey(index)) switch
            {
                (true, true) => UploadProbeBasis.HashAndTags,
                (true, false) => UploadProbeBasis.Hash,
                (false, true) => UploadProbeBasis.Tags,
                _ => UploadProbeBasis.None,
            };

            verdicts.Add(new UploadProbeMatchDto(
                files[index].FileName,
                verdict,
                basis,
                trackId == Guid.Empty ? null : matched.GetValueOrDefault(trackId)));
        }

        return new UploadProbeResultDto(verdicts);
    }

    private async Task<Dictionary<int, Guid>> MatchByTagsAsync(
        Dictionary<int, TagKeys> candidates, CancellationToken ct)
    {
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
}
