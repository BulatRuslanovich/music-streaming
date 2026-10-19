// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Domain.Entities;

namespace App.Recommendations.Embeddings;

public readonly record struct DuplicateGroup(IReadOnlyList<Guid> TrackIds, double Similarity);

// Дубликаты по звучанию: одна и та же запись, загруженная дважды — в другом качестве, с другими тегами
// или с другим названием файла. Хеш файла таких не ловит, а эмбеддинг CLAP у них почти совпадает.
// Сравниваются только треки близкой длительности (окно по отсортированным длительностям), так что
// проход линеен по библиотеке, а не квадратичен.
public static class DuplicateDetector
{
    // На живой библиотеке: та же запись, пережатая в другой битрейт, — 0.945; самые похожие разные
    // песни — 0.83. Порог посередине, а окно длительностей отсекает остальное (две части одной
    // пьесы звучат на 0.99, но длятся по-разному).
    public const double SoundAlike = 0.92;

    // Если совпадают ещё и «артист|название», хватает и меньшего сходства: ремастер, другая громкость.
    public const double SoundAlikeSameSong = 0.88;

    private const double DurationToleranceSeconds = 3;
    private const double DurationToleranceShare = 0.03;

    public static IReadOnlyList<DuplicateGroup> Find(
        EmbeddingSnapshot snapshot,
        IReadOnlyDictionary<Guid, int> durations,
        IReadOnlySet<(Guid First, Guid Second)> dismissed)
    {
        var rows = Enumerable.Range(0, snapshot.Count)
            .Where(row => durations.ContainsKey(snapshot.MetaAt(row).TrackId))
            .OrderBy(row => durations[snapshot.MetaAt(row).TrackId])
            .ToArray();

        var parent = new Dictionary<int, int>();
        var weakest = new Dictionary<int, double>();

        int Root(int row)
        {
            while (parent.TryGetValue(row, out var up) && up != row)
                row = parent[row] = parent.GetValueOrDefault(up, up);

            return row;
        }

        for (var i = 0; i < rows.Length; i++)
        {
            var a = snapshot.MetaAt(rows[i]);
            var durationA = durations[a.TrackId];
            var tolerance = Math.Max(DurationToleranceSeconds, durationA * DurationToleranceShare);

            for (var j = i + 1; j < rows.Length; j++)
            {
                var b = snapshot.MetaAt(rows[j]);
                if (durations[b.TrackId] - durationA > tolerance)
                    break;

                var similarity = snapshot.Between(rows[i], rows[j]);
                var sameSong = !string.IsNullOrEmpty(a.SongKey) && a.SongKey == b.SongKey;

                if (similarity < (sameSong ? SoundAlikeSameSong : SoundAlike))
                    continue;

                if (dismissed.Contains(DuplicateDismissal.Ordered(a.TrackId, b.TrackId)))
                    continue;

                var rootA = Root(rows[i]);
                var rootB = Root(rows[j]);
                var floor = Math.Min(
                    similarity,
                    Math.Min(weakest.GetValueOrDefault(rootA, 1), weakest.GetValueOrDefault(rootB, 1)));

                parent[rootA] = rootA;
                parent[rootB] = rootA;
                weakest[rootA] = floor;
            }
        }

        return parent.Keys
            .GroupBy(Root)
            .Select(group => new DuplicateGroup(
                [.. group.Select(row => snapshot.MetaAt(row).TrackId)],
                weakest.GetValueOrDefault(group.Key, 1)))
            .Where(group => group.TrackIds.Count > 1)
            .OrderByDescending(group => group.Similarity)
            .ToList();
    }
}
