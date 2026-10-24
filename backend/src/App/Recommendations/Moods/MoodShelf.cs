// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Dtos;
using App.Recommendations.Embeddings;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Recommendations.Moods;

// Настроения библиотеки для главной. Лицо настроения — обложки альбомов, которые подходят ему
// сильнее всего: плитка окрашивается той музыкой, что в ней заиграет, а не условным цветом.
public class MoodShelf(ApplicationDbContext db, MoodCatalog moods, EmbeddingIndex index)
{
    public const int PerMood = 3;

    // Сколько лучших треков настроения перебирать в поисках разных альбомов с обложкой.
    private const int Candidates = 60;

    public async Task<IReadOnlyList<MoodDto>> GetAsync(CancellationToken ct)
    {
        var snapshot = index.Snapshot();
        var available = moods.AvailableIn(snapshot).ToList();
        if (available.Count == 0)
            return [];

        var ranked = available.ToDictionary(mood => mood.Key, mood => TopMembers(moods.ScoresIn(snapshot, mood), snapshot));
        var trackIds = ranked.Values.SelectMany(ids => ids).ToHashSet();

        var albums = await db.Tracks.AsNoTracking()
            .Where(track => trackIds.Contains(track.Id) && track.AlbumId != null && track.Album!.CoverPath != null)
            .Select(track => new { track.Id, AlbumId = track.AlbumId!.Value })
            .ToDictionaryAsync(track => track.Id, track => track.AlbumId, ct);

        return [.. available.Select(mood => new MoodDto(mood.Key, Pick(ranked[mood.Key], albums)))];
    }

    // Подходящие треки, самые подходящие первыми.
    private static IReadOnlyList<Guid> TopMembers(MoodScores scores, EmbeddingSnapshot snapshot) =>
        [.. Enumerable.Range(0, scores.Members.Length)
            .Where(row => scores.Members[row])
            .OrderByDescending(row => scores.Ranks[row])
            .Take(Candidates)
            .Select(row => snapshot.MetaAt(row).TrackId)];

    // Первые PerMood разных альбомов с обложкой в порядке треков.
    internal static IReadOnlyList<Guid> Pick(IEnumerable<Guid> tracks, IReadOnlyDictionary<Guid, Guid> albumByTrack) =>
        [.. tracks
            .Where(albumByTrack.ContainsKey)
            .Select(track => albumByTrack[track])
            .Distinct()
            .Take(PerMood)];
}
