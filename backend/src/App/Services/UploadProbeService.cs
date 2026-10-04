// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Infrastructure.Persistence;
using App.Abstractions;
using App.Common;
using App.Dtos;
using Microsoft.EntityFrameworkCore;
using Domain.Common;

namespace App.Services;

public class UploadProbeService(ApplicationDbContext db, ICurrentUser currentUser)
{
    private const int HashLength = 64;

    private sealed record TagKeys(string TitleKey, HashSet<string> ArtistKeys);

    private sealed record TagMatch(Guid TrackId, bool Lossless);

    public async Task<UploadProbeResultDto> ProbeAsync(IReadOnlyList<UploadProbeFileDto> files, CancellationToken ct)
    {
        if (files.Count == 0)
            return new UploadProbeResultDto([]);

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

        var matched = await db.TracksByIdAsync(
            currentUser.Id, byHash.Values.Concat(byTags.Values.Select(match => match.TrackId)), ct);

        var verdicts = new List<UploadProbeMatchDto>(files.Count);
        for (var index = 0; index < files.Count; index++)
        {
            var (verdict, trackId) = byHash.TryGetValue(index, out var exact)
                ? (UploadProbeVerdict.Duplicate, exact)
                : byTags.TryGetValue(index, out var sameTags)
                    ? (TagVerdict(files[index].FileName, sameTags), sameTags.TrackId)
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

    // Совпали название и исполнитель — это тот же трек, если только файл не лучше по качеству:
    // FLAC поверх lossy-копии сервер заменит на месте.
    private static UploadProbeVerdict TagVerdict(string fileName, TagMatch match) =>
        !match.Lossless && AudioUpload.For(fileName)?.Extension == ".flac"
            ? UploadProbeVerdict.Upgrade
            : UploadProbeVerdict.Duplicate;

    private async Task<Dictionary<int, TagMatch>> MatchByTagsAsync(
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
                t.Codec,
                ArtistKeys = t.TrackArtists.Select(ta => ta.Artist!.NormalizedName).ToList(),
            })
            .ToListAsync(ct);

        var byTitle = sameTitle
            .GroupBy(t => t.NormalizedTitle, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var matches = new Dictionary<int, TagMatch>();
        foreach (var (index, candidate) in candidates)
        {
            if (!byTitle.TryGetValue(candidate.TitleKey, out var sameTitleTracks))
                continue;

            var sameTrack = sameTitleTracks.FindAll(t => t.ArtistKeys.Any(candidate.ArtistKeys.Contains));
            if (sameTrack.Count == 0)
                continue;

            // Если в библиотеке есть lossless-копия, сравнивать надо с ней: лучше она уже не станет.
            var best = sameTrack.Find(t => AudioUpload.IsLossless(t.Codec)) ?? sameTrack[0];
            matches[index] = new TagMatch(best.Id, AudioUpload.IsLossless(best.Codec));
        }

        return matches;
    }
}
