// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Xunit;
using App.Recommendations.Embeddings;

namespace UnitTests;

public class LibraryMapLayoutTests
{
    private const int Dimension = 24;

    [Fact]
    public void An_empty_library_has_an_empty_map() =>
        Assert.Equal(0, LibraryMapLayout.Compute(EmbeddingSnapshot.Empty).Count);

    [Fact]
    public void Every_track_lands_inside_the_unit_square()
    {
        var layout = LibraryMapLayout.Compute(Clusters(clusters: 3, perCluster: 40, seed: 1));

        Assert.Equal(120, layout.Count);
        Assert.All(layout.X, x => Assert.InRange(x, 0f, 1f));
        Assert.All(layout.Y, y => Assert.InRange(y, 0f, 1f));
    }

    [Fact]
    public void Tracks_that_sound_alike_end_up_together()
    {
        var snapshot = Clusters(clusters: 4, perCluster: 30, seed: 2);
        var layout = LibraryMapLayout.Compute(snapshot);

        double within = 0, between = 0;
        int withinPairs = 0, betweenPairs = 0;

        for (var a = 0; a < layout.Count; a++)
        {
            for (var b = a + 1; b < layout.Count; b++)
            {
                var distance = Math.Sqrt(Math.Pow(layout.X[a] - layout.X[b], 2) + Math.Pow(layout.Y[a] - layout.Y[b], 2));

                if (a / 30 == b / 30)
                {
                    within += distance;
                    withinPairs++;
                }
                else
                {
                    between += distance;
                    betweenPairs++;
                }
            }
        }

        Assert.True(within / withinPairs * 3 < between / betweenPairs);
    }

    [Fact]
    public void The_same_library_gives_the_same_map()
    {
        var snapshot = Clusters(clusters: 3, perCluster: 20, seed: 3);

        var first = LibraryMapLayout.Compute(snapshot);
        var second = LibraryMapLayout.Compute(snapshot);

        Assert.Equal(first.X, second.X);
        Assert.Equal(first.Y, second.Y);
    }

    // Новый трек встаёт к своим соседям, а не перекраивает карту.
    [Fact]
    public void Adding_a_track_keeps_the_rest_of_the_map_in_place()
    {
        var before = Clusters(clusters: 3, perCluster: 30, seed: 4);
        var previous = LibraryMapLayout.Compute(before);

        var after = Clusters(clusters: 3, perCluster: 30, seed: 4, extra: 1);
        var layout = LibraryMapLayout.Compute(after, previous);

        var moved = 0.0;
        for (var row = 0; row < previous.Count; row++)
        {
            var index = Array.IndexOf(layout.TrackIds, previous.TrackIds[row]);
            moved += Math.Sqrt(Math.Pow(layout.X[index] - previous.X[row], 2) + Math.Pow(layout.Y[index] - previous.Y[row], 2));
        }

        Assert.True(moved / previous.Count < 0.1);
    }

    private static EmbeddingSnapshot Clusters(int clusters, int perCluster, int seed, int extra = 0)
    {
        var random = new Random(seed);
        var centres = Enumerable.Range(0, clusters)
            .Select(_ => Vectors.Unit([.. Enumerable.Range(0, Dimension).Select(_ => (float)(random.NextDouble() - 0.5))]))
            .ToArray();

        var rows = new List<float[]>();
        for (var cluster = 0; cluster < clusters; cluster++)
        {
            for (var i = 0; i < perCluster; i++)
                rows.Add(Vectors.Unit([.. centres[cluster].Select(value => value + (float)(random.NextDouble() - 0.5) * 0.15f)]));
        }

        for (var i = 0; i < extra; i++)
            rows.Add(Vectors.Unit([.. centres[0].Select(value => value + (float)(random.NextDouble() - 0.5) * 0.15f)]));

        var ids = new Random(seed * 31);
        return new EmbeddingSnapshot(
            [.. rows.SelectMany(row => row)],
            [.. rows.Select((_, index) => new TrackVectorMeta(Id(ids, index), Guid.Empty, "", "", DateTimeOffset.UnixEpoch, 0))],
            Dimension);
    }

    private static Guid Id(Random random, int index)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(index).CopyTo(bytes, 0);
        return new Guid(bytes);
    }
}
