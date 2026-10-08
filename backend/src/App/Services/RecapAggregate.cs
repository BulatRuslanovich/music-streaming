// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Numerics.Tensors;
using App.Recommendations.Embeddings;
using App.Recommendations.Moods;

namespace App.Services;

// Одна законченная попытка послушать трек: дослушал или переключил. LocalTime — по часам слушателя.
public readonly record struct RecapPlay(Guid TrackId, DateTime LocalTime, int Seconds);

public sealed record RecapTrackFacts(IReadOnlyList<Guid> ArtistIds, Guid? GenreId);

public sealed record RecapRanking(Guid Id, int Plays, long Seconds);

public sealed record RecapTotals(
    long Seconds,
    int Plays,
    int DistinctTracks,
    int DistinctArtists,
    IReadOnlyList<RecapRanking> Tracks,
    // Все артисты месяца по убыванию времени: из них же выбираются открытия.
    IReadOnlyList<RecapRanking> Artists,
    IReadOnlyList<(Guid GenreId, double Share)> Genres,
    long[] DaySeconds,
    long[] HourSeconds);

// Чистая арифметика итогов месяца: без базы, чтобы её можно было проверить на руках.
public static class RecapAggregate
{
    public static RecapTotals Totals(
        IReadOnlyList<RecapPlay> plays,
        IReadOnlyDictionary<Guid, RecapTrackFacts> facts,
        int year,
        int month,
        int top)
    {
        var days = new long[DateTime.DaysInMonth(year, month)];
        var hours = new long[24];
        var tracks = new Dictionary<Guid, (int Plays, long Seconds)>();
        var artists = new Dictionary<Guid, (int Plays, long Seconds)>();
        var genres = new Dictionary<Guid, long>();
        long total = 0;
        var counted = 0;

        foreach (var play in plays)
        {
            total += play.Seconds;
            days[play.LocalTime.Day - 1] += play.Seconds;
            hours[play.LocalTime.Hour] += play.Seconds;

            // Короткая попытка добавляет время, но прослушиванием не считается — как в истории.
            var full = play.Seconds >= HistoryService.ThresholdSeconds ? 1 : 0;
            counted += full;

            var track = tracks.GetValueOrDefault(play.TrackId);
            tracks[play.TrackId] = (track.Plays + full, track.Seconds + play.Seconds);

            if (!facts.TryGetValue(play.TrackId, out var fact))
                continue;

            foreach (var artistId in fact.ArtistIds)
            {
                var artist = artists.GetValueOrDefault(artistId);
                artists[artistId] = (artist.Plays + full, artist.Seconds + play.Seconds);
            }

            if (fact.GenreId is { } genreId)
                genres[genreId] = genres.GetValueOrDefault(genreId) + play.Seconds;
        }

        var genreTotal = genres.Values.Sum();

        return new RecapTotals(
            total,
            counted,
            tracks.Count(pair => pair.Value.Plays > 0),
            artists.Count(pair => pair.Value.Plays > 0),
            [.. tracks
                .Where(pair => pair.Value.Plays > 0)
                .OrderByDescending(pair => pair.Value.Plays)
                .ThenByDescending(pair => pair.Value.Seconds)
                .ThenBy(pair => pair.Key)
                .Take(top)
                .Select(pair => new RecapRanking(pair.Key, pair.Value.Plays, pair.Value.Seconds))],
            [.. artists
                .Where(pair => pair.Value.Plays > 0)
                .OrderByDescending(pair => pair.Value.Seconds)
                .ThenByDescending(pair => pair.Value.Plays)
                .ThenBy(pair => pair.Key)
                .Select(pair => new RecapRanking(pair.Key, pair.Value.Plays, pair.Value.Seconds))],
            genreTotal == 0
                ? []
                : [.. genres
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key)
                    .Take(top)
                    .Select(pair => (pair.Key, pair.Value / (double)genreTotal))],
            days,
            hours);
    }

    // Доля времени под каждым настроением: трек относится к тому настроению, где его ранг выше всего.
    public static IReadOnlyList<(string Key, double Share)> MoodShares(
        IReadOnlyList<RecapPlay> plays,
        EmbeddingSnapshot snapshot,
        IReadOnlyList<(string Key, float[] Ranks)> moods)
    {
        if (moods.Count == 0 || snapshot.IsEmpty)
            return [];

        var seconds = new long[moods.Count];
        var ranks = moods.Select(mood => mood.Ranks).ToArray();

        foreach (var play in plays)
        {
            var row = snapshot.RowOf(play.TrackId);
            if (row < 0)
                continue;

            seconds[MoodCatalog.Dominant(ranks, row)] += play.Seconds;
        }

        var total = seconds.Sum();
        if (total == 0)
            return [];

        return [.. Enumerable.Range(0, moods.Count)
            .Where(index => seconds[index] > 0)
            .OrderByDescending(index => seconds[index])
            .Select(index => (moods[index].Key, seconds[index] / (double)total))];
    }

    // «Звук месяца»: трек, ближе всего стоящий к центру всего, что звучало, с весом по времени.
    public static Guid? SoundOf(IReadOnlyList<RecapPlay> plays, EmbeddingSnapshot snapshot)
    {
        if (snapshot.IsEmpty)
            return null;

        var centre = new float[snapshot.Dimension];
        var rows = new HashSet<int>();

        foreach (var play in plays)
        {
            var row = snapshot.RowOf(play.TrackId);
            if (row < 0 || play.Seconds <= 0)
                continue;

            rows.Add(row);
            TensorPrimitives.MultiplyAdd(snapshot.Vector(row), play.Seconds, centre, centre);
        }

        if (rows.Count == 0)
            return null;

        VectorMath.NormalizeInPlace(centre);

        return snapshot.MetaAt(rows
            .OrderByDescending(row => TensorPrimitives.Dot(centre, snapshot.Vector(row)))
            .ThenBy(row => row)
            .First()).TrackId;
    }
}
