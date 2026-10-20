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

    // Радиус кучки для подписи настроения — около десятой доли карты.
    private const float ClusterRadius = 0.08f;

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
                .Select(row => positions.TryGetValue(snapshot.MetaAt(row).TrackId, out var spot)
                    ? (spot.X, spot.Y, Weight: scores.Ranks[row])
                    : ((float X, float Y, float Weight)?)null)
                .OfType<(float X, float Y, float Weight)>()
                .Take(LandmarkTracks)
                .ToList();

            if (spots.Count == 0)
                continue;

            var (x, y) = DensestSpot(spots);
            landmarks.Add(new MapLandmarkDto(mood.Key, MathF.Round(x, 4), MathF.Round(y, 4)));
        }

        return landmarks;
    }

    // Подпись настроения — там, где его треки гуще всего, а не в середине их облака: карта раскладывает
    // треки по звучанию, и одно настроение лежит несколькими кучками в разных углах. Медиана таких кучек
    // попадает в пустой центр карты, и подписи всех настроений сбиваются туда же. Здесь каждый трек
    // оценивает, сколько рядом других треков этого настроения (с весом — насколько они ему подходят),
    // подпись встаёт в самую плотную кучку, в её взвешенный центр.
    internal static (float X, float Y) DensestSpot(IReadOnlyList<(float X, float Y, float Weight)> spots)
    {
        var best = 0;
        var bestDensity = float.MinValue;

        for (var i = 0; i < spots.Count; i++)
        {
            var density = 0f;
            foreach (var other in spots)
                density += other.Weight * Kernel(spots[i], other);

            if (density > bestDensity)
            {
                bestDensity = density;
                best = i;
            }
        }

        float x = 0, y = 0, total = 0;
        foreach (var other in spots)
        {
            var weight = other.Weight * Kernel(spots[best], other);
            x += other.X * weight;
            y += other.Y * weight;
            total += weight;
        }

        return total > 0 ? (x / total, y / total) : (spots[best].X, spots[best].Y);
    }

    private static float Kernel((float X, float Y, float Weight) a, (float X, float Y, float Weight) b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Exp(-(dx * dx + dy * dy) / (2 * ClusterRadius * ClusterRadius));
    }
}
