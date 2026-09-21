// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Recommendations.Embeddings;
using Xunit;

namespace MusicStreaming.UnitTests.Recommendations;

public class EmbeddingSnapshotTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_empty_index_answers_every_question_without_throwing()
    {
        var snapshot = EmbeddingSnapshot.Empty;

        Assert.True(snapshot.IsEmpty);
        Assert.Equal(0, snapshot.Count);
        Assert.Equal(-1, snapshot.RowOf(Guid.NewGuid()));
        Assert.Empty(snapshot.TopK([1f, 0f], 5));
        Assert.Equal(0, snapshot.Between(0, 1));
    }

    [Fact]
    public void Top_k_agrees_with_a_full_sort_of_every_similarity()
    {
        var snapshot = Build(RandomUnitRows(count: 40, dimension: 8, seed: 7));
        var query = Unit([0.3f, -0.9f, 0.1f, 0.4f, 0f, 0.2f, -0.1f, 0.5f]);

        var similarities = snapshot.SimilaritiesTo(query);
        var expected = Enumerable.Range(0, snapshot.Count)
            .OrderByDescending(row => similarities[row])
            .ThenBy(row => row)
            .Take(6)
            .ToArray();

        var actual = snapshot.TopK(query, 6).Select(hit => hit.Row).ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Top_k_never_returns_an_excluded_track()
    {
        var snapshot = Build(RandomUnitRows(count: 20, dimension: 8, seed: 11));
        var query = snapshot.Vector(0).ToArray();

        var banned = Enumerable.Range(0, 5).Select(row => snapshot.MetaAt(row).TrackId).ToHashSet();
        var hits = snapshot.TopK(query, 10, banned);

        Assert.Equal(10, hits.Count);
        Assert.DoesNotContain(hits, hit => banned.Contains(hit.TrackId));
    }

    [Fact]
    public void Ties_are_broken_by_the_lower_row_so_results_are_reproducible()
    {
        // Три одинаковых вектора: любая из строк «правильная», но выбор должен быть один и тот же.
        var rows = new[] { Unit([1f, 0f]), Unit([1f, 0f]), Unit([1f, 0f]), Unit([0f, 1f]) };
        var snapshot = Build(rows);

        var hits = snapshot.TopK([1f, 0f], 2);

        Assert.Equal([0, 1], hits.Select(hit => hit.Row));
    }

    [Fact]
    public void A_track_is_perfectly_similar_to_itself()
    {
        var snapshot = Build(RandomUnitRows(count: 12, dimension: 16, seed: 3));

        Assert.Equal(1.0, snapshot.Between(4, 4), precision: 5);
    }

    [Fact]
    public void Clones_union_byte_identical_files_and_the_same_song_under_another_file()
    {
        var shared = "same-bytes";
        var meta = new[]
        {
            MetaFor(0, Guid.NewGuid()) with { ContentHash = shared, SongKey = "a|one" },
            MetaFor(1, Guid.NewGuid()) with { ContentHash = shared, SongKey = "b|two" },
            MetaFor(2, Guid.NewGuid()) with { ContentHash = "other", SongKey = "a|one" },
            MetaFor(3, Guid.NewGuid()) with { ContentHash = "lone", SongKey = "c|three" },
        };

        var snapshot = Build(RandomUnitRows(count: 4, dimension: 4, seed: 2), meta);
        var family = snapshot.CloneIds(meta[0].TrackId).ToHashSet();

        Assert.Contains(meta[0].TrackId, family);
        Assert.Contains(meta[1].TrackId, family);   // тот же файл
        Assert.Contains(meta[2].TrackId, family);   // та же песня
        Assert.DoesNotContain(meta[3].TrackId, family);
    }

    [Fact]
    public void An_unknown_track_is_its_own_only_clone()
    {
        var snapshot = Build(RandomUnitRows(count: 3, dimension: 4, seed: 1));
        var stranger = Guid.NewGuid();

        Assert.Equal([stranger], snapshot.CloneIds(stranger));
    }

    [Fact]
    public void A_song_key_needs_both_halves()
    {
        Assert.Equal("radiohead|creep", EmbeddingSnapshot.SongKeyOf("  Radiohead ", "Creep"));
        Assert.Equal(string.Empty, EmbeddingSnapshot.SongKeyOf("Radiohead", " "));
        Assert.Equal(string.Empty, EmbeddingSnapshot.SongKeyOf(null, "Creep"));
    }

    [Fact]
    public void Seed_similarity_takes_the_best_weighted_seed_and_ignores_the_track_itself()
    {
        var snapshot = Build([Unit([1f, 0f]), Unit([0f, 1f]), Unit([0.6f, 0.8f])]);

        // Строка 2 ближе к строке 1 (0.8), но сид 0 весит вдвое больше: 1.0 * 0.6 против 0.5 * 0.8.
        var best = snapshot.SeedSimilarity(2, [(0, 1.0), (1, 0.5)]);

        Assert.Equal(0.6, best, precision: 5);
        Assert.Equal(0, snapshot.SeedSimilarity(0, [(0, 1.0)]));
    }

    [Fact]
    public void Percentiles_put_the_worst_at_zero_and_the_best_at_one()
    {
        var percentiles = EmbeddingSnapshot.ToPercentiles([0.4f, 0.1f, 0.9f, 0.6f]);

        Assert.Equal(0f, percentiles[1]);
        Assert.Equal(1f, percentiles[2]);
        Assert.True(percentiles[0] < percentiles[3]);
    }

    [Fact]
    public void The_parallel_path_agrees_with_the_serial_one()
    {
        // Выше порога в 1500 строк проход разбивается на блоки; результат обязан совпасть.
        var snapshot = Build(RandomUnitRows(count: 2000, dimension: 16, seed: 21));
        var query = Unit([.. Enumerable.Range(0, 16).Select(i => (float)Math.Sin(i))]);

        var similarities = snapshot.SimilaritiesTo(query);

        for (var row = 0; row < snapshot.Count; row++)
            Assert.Equal(VectorMath.Dot(query, snapshot.Vector(row)), similarities[row], precision: 5);
    }

    private static EmbeddingSnapshot Build(float[][] rows, TrackVectorMeta[]? meta = null)
    {
        var dimension = rows[0].Length;
        var matrix = new float[rows.Length * dimension];

        for (var row = 0; row < rows.Length; row++)
            rows[row].CopyTo(matrix, row * dimension);

        meta ??= [.. Enumerable.Range(0, rows.Length).Select(row => MetaFor(row, Guid.NewGuid()))];
        return new EmbeddingSnapshot(matrix, meta, dimension, Now);
    }

    private static TrackVectorMeta MetaFor(int row, Guid artistId) => new(
        TrackId: Guid.NewGuid(),
        ArtistId: artistId,
        ContentHash: $"hash-{row}",
        SongKey: $"artist-{row}|title-{row}",
        CreatedAt: Now,
        ClusterId: -1,
        ShownCount: 0,
        SkippedEarlyCount: 0);

    private static float[][] RandomUnitRows(int count, int dimension, int seed)
    {
        var random = new Random(seed);

        return [.. Enumerable.Range(0, count).Select(_ =>
            Unit([.. Enumerable.Range(0, dimension).Select(_ => (float)(random.NextDouble() * 2 - 1))]))];
    }

    private static float[] Unit(float[] values) => VectorMath.Normalized(values);
}
