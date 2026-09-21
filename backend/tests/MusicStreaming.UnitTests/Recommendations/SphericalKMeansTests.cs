// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Recommendations.Embeddings;
using Xunit;

namespace MusicStreaming.UnitTests.Recommendations;

public class SphericalKMeansTests
{
    [Fact]
    public void Well_separated_groups_land_in_separate_clusters()
    {
        // Три плотных облака вокруг осей: разделение должно быть безошибочным.
        var (matrix, count, dimension) = Blobs(centres: 3, perCentre: 12, dimension: 3, spread: 0.05, seed: 4);

        var result = SphericalKMeans.Cluster(matrix, count, dimension, k: 3);

        Assert.Equal(3, result.ClusterCount);
        Assert.Equal(count, result.Labels.Length);

        for (var centre = 0; centre < 3; centre++)
        {
            var labels = result.Labels.Skip(centre * 12).Take(12).Distinct().ToArray();
            Assert.Single(labels);
        }

        Assert.Equal(3, result.Labels.Distinct().Count());
    }

    [Fact]
    public void Inertia_stays_inside_the_range_a_mean_cosine_distance_can_take()
    {
        var (matrix, count, dimension) = Blobs(centres: 3, perCentre: 8, dimension: 8, spread: 0.4, seed: 9);

        var result = SphericalKMeans.Cluster(matrix, count, dimension, k: 3);

        Assert.InRange(result.Inertia, 0.0, 2.0);
    }

    [Fact]
    public void Tight_clusters_have_lower_inertia_than_scattered_ones()
    {
        var (tight, count, dimension) = Blobs(centres: 3, perCentre: 10, dimension: 6, spread: 0.02, seed: 13);
        var (loose, _, _) = Blobs(centres: 3, perCentre: 10, dimension: 6, spread: 0.9, seed: 13);

        var tightResult = SphericalKMeans.Cluster(tight, count, dimension, k: 3);
        var looseResult = SphericalKMeans.Cluster(loose, count, dimension, k: 3);

        Assert.True(tightResult.Inertia < looseResult.Inertia);
    }

    [Fact]
    public void The_same_seed_gives_the_same_labels()
    {
        var (matrix, count, dimension) = Blobs(centres: 4, perCentre: 9, dimension: 8, spread: 0.5, seed: 17);

        var first = SphericalKMeans.Cluster(matrix, count, dimension, k: 4, seed: 42);
        var second = SphericalKMeans.Cluster(matrix, count, dimension, k: 4, seed: 42);

        Assert.Equal(first.Labels, second.Labels);
    }

    [Fact]
    public void Asking_for_more_clusters_than_rows_clamps_to_the_row_count()
    {
        var (matrix, count, dimension) = Blobs(centres: 2, perCentre: 2, dimension: 4, spread: 0.1, seed: 2);

        var result = SphericalKMeans.Cluster(matrix, count, dimension, k: 50);

        Assert.Equal(count, result.ClusterCount);
        Assert.All(result.Labels, label => Assert.InRange(label, 0, count - 1));
    }

    [Fact]
    public void Centroids_come_back_as_unit_vectors()
    {
        var (matrix, count, dimension) = Blobs(centres: 3, perCentre: 10, dimension: 5, spread: 0.3, seed: 8);

        var result = SphericalKMeans.Cluster(matrix, count, dimension, k: 3);

        for (var cluster = 0; cluster < result.ClusterCount; cluster++)
        {
            var centroid = result.Centroids.AsSpan(cluster * dimension, dimension);
            var norm = Math.Sqrt(VectorMath.Dot(centroid, centroid));

            Assert.Equal(1.0, norm, precision: 4);
        }
    }

    [Fact]
    public void An_empty_matrix_produces_nothing_rather_than_throwing()
    {
        var result = SphericalKMeans.Cluster([], count: 0, dimension: 8, k: 4);

        Assert.Empty(result.Labels);
        Assert.Equal(0, result.ClusterCount);
    }

    /// <summary>Строки группами вокруг осевых направлений, с шумом заданной силы.</summary>
    private static (float[] Matrix, int Count, int Dimension) Blobs(
        int centres, int perCentre, int dimension, double spread, int seed)
    {
        var random = new Random(seed);
        var count = centres * perCentre;
        var matrix = new float[count * dimension];

        for (var centre = 0; centre < centres; centre++)
        {
            for (var member = 0; member < perCentre; member++)
            {
                var row = centre * perCentre + member;
                var target = matrix.AsSpan(row * dimension, dimension);

                for (var i = 0; i < dimension; i++)
                    target[i] = (float)((i == centre % dimension ? 1.0 : 0.0) + (random.NextDouble() * 2 - 1) * spread);

                VectorMath.NormalizeInPlace(target);
            }
        }

        return (matrix, count, dimension);
    }
}
