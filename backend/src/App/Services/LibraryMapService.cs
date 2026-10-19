// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Abstractions;
using App.Dtos;
using App.Recommendations.Embeddings;
using App.Recommendations.Moods;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Services;

public class LibraryMapService(
    ApplicationDbContext db,
    EmbeddingIndex index,
    LibraryMapCache cache,
    MoodCatalog moods,
    ICurrentUser currentUser)
{
    private const int LandmarkTracks = 30;

    public async Task<LibraryMapDto> GetMapAsync(CancellationToken ct)
    {
        var snapshot = index.Snapshot();
        var layout = await cache.GetAsync(snapshot, ct);

        var tracks = await db.Tracks.AsNoTracking()
            .Select(track => new
            {
                track.Id,
                track.Title,
                Artist = track.Artist!.Name,
                track.AlbumId,
                HasCover = track.Album != null && track.Album.CoverPath != null,
                Genre = track.Genre == null ? null : track.Genre.Name,
            })
            .ToDictionaryAsync(track => track.Id, ct);

        var plays = await db.ListeningHistory.AsNoTracking()
            .Where(entry => entry.UserId == currentUser.Id)
            .GroupBy(entry => entry.TrackId)
            .Select(group => new { TrackId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.TrackId, group => group.Count, ct);

        var points = new List<MapPointDto>(layout.Count);
        var positions = new Dictionary<Guid, (float X, float Y)>(layout.Count);

        for (var i = 0; i < layout.Count; i++)
        {
            var id = layout.TrackIds[i];
            if (!tracks.TryGetValue(id, out var track))
                continue;

            var x = MathF.Round(layout.X[i], 4);
            var y = MathF.Round(layout.Y[i], 4);
            positions[id] = (x, y);

            points.Add(new MapPointDto(
                id, x, y, track.Title, track.Artist, track.AlbumId, track.HasCover, track.Genre,
                plays.GetValueOrDefault(id)));
        }

        return new LibraryMapDto(points, Landmarks(snapshot, positions), tracks.Count);
    }

    private List<MapLandmarkDto> Landmarks(
        EmbeddingSnapshot snapshot, IReadOnlyDictionary<Guid, (float X, float Y)> positions)
    {
        var landmarks = new List<MapLandmarkDto>();

        foreach (var mood in moods.AvailableIn(snapshot))
        {
            var scores = moods.ScoresIn(snapshot, mood);

            var spots = Enumerable.Range(0, snapshot.Count)
                .Where(row => scores.Members[row])
                .OrderByDescending(row => scores.Ranks[row])
                .Select(row => positions.TryGetValue(snapshot.MetaAt(row).TrackId, out var spot) ? spot : ((float, float)?)null)
                .OfType<(float X, float Y)>()
                .Take(LandmarkTracks)
                .ToList();

            if (spots.Count == 0)
                continue;

            landmarks.Add(new MapLandmarkDto(mood.Key, Median(spots.Select(spot => spot.X)), Median(spots.Select(spot => spot.Y))));
        }

        return landmarks;
    }

    private static float Median(IEnumerable<float> values)
    {
        var sorted = values.Order().ToArray();
        return sorted[sorted.Length / 2];
    }
}
