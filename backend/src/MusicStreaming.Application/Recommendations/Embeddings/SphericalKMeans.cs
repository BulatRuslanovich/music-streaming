// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Numerics.Tensors;

namespace MusicStreaming.Application.Recommendations.Embeddings;

/// <param name="Labels">Метка кластера для каждой строки матрицы.</param>
/// <param name="Centroids">row-major, ClusterCount * Dimension, единичной длины.</param>
/// <param name="Inertia">Среднее косинусное расстояние до своего центроида, 0..2.</param>
public record ClusteringResult(int[] Labels, float[] Centroids, int ClusterCount, double Inertia);

/// <summary>
/// Сферический k-means: вектора единичной длины, поэтому назначение по максимуму скалярного
/// произведения и есть назначение по косинусу. Порт index/clusters.py из musik.
/// </summary>
public static class SphericalKMeans
{
    public const int DefaultSeed = 42;
    public const int DefaultMaxIterations = 50;

    public static ClusteringResult Cluster(
        ReadOnlySpan<float> matrix,
        int count,
        int dimension,
        int k,
        int maxIterations = DefaultMaxIterations,
        int seed = DefaultSeed)
    {
        if (count <= 0 || dimension <= 0)
            return new ClusteringResult([], [], 0, 0);

        k = Math.Max(1, Math.Min(k, count));

        var random = new Random(seed);
        var centroids = new float[k * dimension];

        // Инициализация: k различных строк как стартовые центроиды.
        foreach (var (slot, row) in SampleDistinct(random, count, k).Index())
            matrix.Slice(row * dimension, dimension).CopyTo(centroids.AsSpan(slot * dimension, dimension));

        var labels = new int[count];
        var previous = new int[count];
        Array.Fill(previous, -1);

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            for (var row = 0; row < count; row++)
            {
                var vector = matrix.Slice(row * dimension, dimension);
                var best = 0;
                var bestScore = float.NegativeInfinity;

                for (var cluster = 0; cluster < k; cluster++)
                {
                    var score = TensorPrimitives.Dot(vector, centroids.AsSpan(cluster * dimension, dimension));
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = cluster;
                    }
                }

                labels[row] = best;
            }

            if (labels.AsSpan().SequenceEqual(previous))
                break;

            labels.CopyTo(previous, 0);

            var accumulator = new double[k * dimension];
            var members = new int[k];

            for (var row = 0; row < count; row++)
            {
                var cluster = labels[row];
                members[cluster]++;

                var vector = matrix.Slice(row * dimension, dimension);
                var offset = cluster * dimension;
                for (var i = 0; i < dimension; i++)
                    accumulator[offset + i] += vector[i];
            }

            for (var cluster = 0; cluster < k; cluster++)
            {
                var target = centroids.AsSpan(cluster * dimension, dimension);

                // Пустой кластер пересеивается случайной строкой — иначе он остался бы пустым
                // навсегда и k по факту уменьшилось бы.
                if (members[cluster] == 0)
                {
                    matrix.Slice(random.Next(count) * dimension, dimension).CopyTo(target);
                    continue;
                }

                var offset = cluster * dimension;
                for (var i = 0; i < dimension; i++)
                    target[i] = (float)(accumulator[offset + i] / members[cluster]);

                VectorMath.NormalizeInPlace(target);
            }
        }

        var inertia = 0.0;
        for (var row = 0; row < count; row++)
        {
            var similarity = TensorPrimitives.Dot(
                matrix.Slice(row * dimension, dimension),
                centroids.AsSpan(labels[row] * dimension, dimension));

            inertia += 1.0 - similarity;
        }

        return new ClusteringResult(labels, centroids, k, inertia / count);
    }

    private static int[] SampleDistinct(Random random, int count, int k)
    {
        if (k >= count)
            return [.. Enumerable.Range(0, count)];

        var chosen = new HashSet<int>(k);
        while (chosen.Count < k)
            chosen.Add(random.Next(count));

        return [.. chosen];
    }
}
