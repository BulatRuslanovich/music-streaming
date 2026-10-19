// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Numerics.Tensors;

namespace App.Recommendations.Embeddings;

// Позиции треков на карте библиотеки, в [0, 1] по обеим осям.
public sealed record MapLayout(Guid[] TrackIds, float[] X, float[] Y)
{
    public static MapLayout Empty { get; } = new([], [], []);

    public int Count => TrackIds.Length;
}

// Карта библиотеки: 512-мерные векторы CLAP укладываются на плоскость так, чтобы похоже звучащие
// треки оказались рядом. Это упрощённый UMAP: граф k ближайших соседей с нечёткими весами,
// затем стохастический спуск, где рёбра графа притягивают точки, а случайные пары — расталкивают.
// Старт — главные компоненты, а при пересчёте — прошлая карта: новые треки встают к своим соседям,
// и библиотека не перетасовывается после каждой загрузки.
public static class LibraryMapLayout
{
    private const int Neighbours = 15;
    private const int NegativeSamples = 5;

    // Параметры кривой притяжения UMAP для min_dist = 0.1, spread = 1.
    private const float CurveA = 1.577f;
    private const float CurveB = 0.895f;

    private const float GradientClip = 4f;
    private const float InitialSpread = 10f;

    public static MapLayout Compute(
        EmbeddingSnapshot snapshot,
        MapLayout? previous = null,
        int seed = 17,
        CancellationToken ct = default)
    {
        var count = snapshot.Count;
        var ids = new Guid[count];
        for (var row = 0; row < count; row++)
            ids[row] = snapshot.MetaAt(row).TrackId;

        if (count == 0)
            return MapLayout.Empty;

        if (count < 4)
        {
            var spots = Enumerable.Range(0, count).Select(row => (float)(row + 1) / (count + 1)).ToArray();
            return new MapLayout(ids, spots, [.. Enumerable.Repeat(0.5f, count)]);
        }

        var (neighbours, distances) = NearestNeighbours(snapshot, Math.Min(Neighbours, count - 1), ct);
        var edges = FuzzyGraph(neighbours, distances);

        var reused = 0;
        var (x, y) = previous is { Count: > 0 }
            ? FromPrevious(ids, previous, neighbours, seed, out reused)
            : PrincipalComponents(snapshot);

        // Если почти вся карта взята из прошлой, её нужно лишь подправить, а не строить заново.
        var settled = reused >= count * 0.8;
        var epochs = settled ? 80 : count <= 10_000 ? 400 : 200;
        var learningRate = settled ? 0.3f : 1f;

        Optimize(x, y, edges, epochs, learningRate, seed, ct);
        Normalize(x, y);

        return new MapLayout(ids, x, y);
    }

    private static (int[][] Neighbours, float[][] Distances) NearestNeighbours(
        EmbeddingSnapshot snapshot, int k, CancellationToken ct)
    {
        var count = snapshot.Count;
        var neighbours = new int[count][];
        var distances = new float[count][];

        Parallel.For(0, count, new ParallelOptions { CancellationToken = ct }, row =>
        {
            var query = snapshot.Vector(row);
            var heap = new PriorityQueue<int, float>(k + 1);

            for (var other = 0; other < count; other++)
            {
                if (other == row)
                    continue;

                var similarity = TensorPrimitives.Dot(query, snapshot.Vector(other));

                if (heap.Count < k)
                    heap.Enqueue(other, similarity);
                else if (heap.TryPeek(out _, out var weakest) && similarity > weakest)
                    heap.EnqueueDequeue(other, similarity);
            }

            var found = new int[heap.Count];
            var spans = new float[heap.Count];

            for (var index = found.Length - 1; heap.TryDequeue(out var other, out var similarity); index--)
            {
                found[index] = other;
                // Векторы нормированы: евклидово расстояние выражается через косинус.
                spans[index] = MathF.Sqrt(MathF.Max(0, 2 - 2 * similarity));
            }

            neighbours[row] = found;
            distances[row] = spans;
        });

        return (neighbours, distances);
    }

    // Вес ребра — насколько сосед близок по меркам окрестности самого трека (ρ — до ближайшего,
    // σ подобрана так, чтобы сумма весов была log2 k). Направленные веса сводятся в симметричные
    // как «хотя бы один считает другого соседом».
    private static List<(int From, int To, float Weight)> FuzzyGraph(int[][] neighbours, float[][] distances)
    {
        var directed = new Dictionary<(int, int), float>();

        for (var row = 0; row < neighbours.Length; row++)
        {
            var spans = distances[row];
            if (spans.Length == 0)
                continue;

            var nearest = spans[0];
            var target = MathF.Log2(spans.Length);
            float low = 0, high = float.PositiveInfinity, sigma = 1;

            for (var step = 0; step < 64; step++)
            {
                var sum = 0f;
                foreach (var span in spans)
                    sum += MathF.Exp(-MathF.Max(0, span - nearest) / sigma);

                if (MathF.Abs(sum - target) < 1e-5f)
                    break;

                if (sum > target)
                {
                    high = sigma;
                    sigma = (low + high) / 2;
                }
                else
                {
                    low = sigma;
                    sigma = float.IsPositiveInfinity(high) ? sigma * 2 : (low + high) / 2;
                }
            }

            for (var index = 0; index < spans.Length; index++)
            {
                var weight = MathF.Exp(-MathF.Max(0, spans[index] - nearest) / MathF.Max(sigma, 1e-6f));
                directed[(row, neighbours[row][index])] = weight;
            }
        }

        var edges = new List<(int, int, float)>(directed.Count);

        foreach (var ((from, to), weight) in directed)
        {
            var back = directed.GetValueOrDefault((to, from));

            if (back > 0 && from > to)
                continue;

            edges.Add((from, to, weight + back - weight * back));
        }

        return edges;
    }

    private static (float[] X, float[] Y) PrincipalComponents(EmbeddingSnapshot snapshot)
    {
        var count = snapshot.Count;
        var dimension = snapshot.Dimension;
        var mean = new float[dimension];

        for (var row = 0; row < count; row++)
            TensorPrimitives.Add(mean, snapshot.Vector(row), mean);

        TensorPrimitives.Divide(mean, count, mean);

        var centred = new float[dimension];
        var first = PowerIteration(snapshot, mean, centred, exclude: null);
        var second = PowerIteration(snapshot, mean, centred, exclude: first);

        var x = new float[count];
        var y = new float[count];

        for (var row = 0; row < count; row++)
        {
            TensorPrimitives.Subtract(snapshot.Vector(row), mean, centred);
            x[row] = TensorPrimitives.Dot(centred, first);
            y[row] = TensorPrimitives.Dot(centred, second);
        }

        Rescale(x, y, InitialSpread);
        return (x, y);
    }

    // Главная компонента без матрицы ковариаций: v ← Σ (xᵢ·v) xᵢ по центрированным векторам.
    private static float[] PowerIteration(EmbeddingSnapshot snapshot, float[] mean, float[] centred, float[]? exclude)
    {
        var dimension = snapshot.Dimension;
        var vector = new float[dimension];

        for (var i = 0; i < dimension; i++)
            vector[i] = MathF.Sin(i * 12.9898f + (exclude is null ? 0 : 78.233f));

        VectorMath.NormalizeInPlace(vector);

        var next = new float[dimension];

        for (var iteration = 0; iteration < 40; iteration++)
        {
            Array.Clear(next);

            for (var row = 0; row < snapshot.Count; row++)
            {
                TensorPrimitives.Subtract(snapshot.Vector(row), mean, centred);
                TensorPrimitives.MultiplyAdd(centred, TensorPrimitives.Dot(centred, vector), next, next);
            }

            if (exclude is not null)
                TensorPrimitives.MultiplyAdd(exclude, -TensorPrimitives.Dot(next, exclude), next, next);

            if (TensorPrimitives.Norm(next) < 1e-12f)
                break;

            VectorMath.NormalizeInPlace(next);
            (vector, next) = (next, vector);
        }

        // Знак компоненты произволен; фиксируем его, чтобы карта не зеркалилась от пересчёта к пересчёту.
        if (vector[TensorPrimitives.IndexOfMaxMagnitude(vector)] < 0)
            TensorPrimitives.Negate(vector, vector);

        return vector;
    }

    private static (float[] X, float[] Y) FromPrevious(
        Guid[] ids, MapLayout previous, int[][] neighbours, int seed, out int reused)
    {
        var known = new Dictionary<Guid, int>(previous.Count);
        for (var index = 0; index < previous.Count; index++)
            known[previous.TrackIds[index]] = index;

        var count = ids.Length;
        var x = new float[count];
        var y = new float[count];
        var placed = new bool[count];
        reused = 0;

        for (var row = 0; row < count; row++)
        {
            if (!known.TryGetValue(ids[row], out var index))
                continue;

            x[row] = (previous.X[index] - 0.5f) * 2 * InitialSpread;
            y[row] = (previous.Y[index] - 0.5f) * 2 * InitialSpread;
            placed[row] = true;
            reused++;
        }

        var random = new Random(seed);

        for (var row = 0; row < count; row++)
        {
            if (placed[row])
                continue;

            float sumX = 0, sumY = 0;
            var found = 0;

            foreach (var neighbour in neighbours[row])
            {
                if (!placed[neighbour])
                    continue;

                sumX += x[neighbour];
                sumY += y[neighbour];
                found++;
            }

            var jitter = InitialSpread * 0.02f;
            x[row] = (found > 0 ? sumX / found : 0) + (float)(random.NextDouble() - 0.5) * jitter;
            y[row] = (found > 0 ? sumY / found : 0) + (float)(random.NextDouble() - 0.5) * jitter;
        }

        return (x, y);
    }

    private static void Optimize(
        float[] x,
        float[] y,
        List<(int From, int To, float Weight)> edges,
        int epochs,
        float learningRate,
        int seed,
        CancellationToken ct)
    {
        if (edges.Count == 0)
            return;

        var count = x.Length;
        var random = new Random(seed);
        var heaviest = edges.Max(edge => edge.Weight);

        // Ребро с весом w обновляется раз в heaviest / w эпох: слабые связи тянут реже.
        var every = edges.Select(edge => heaviest / MathF.Max(edge.Weight, 1e-6f)).ToArray();
        var nextAt = (float[])every.Clone();
        var negativeEvery = every.Select(value => value / NegativeSamples).ToArray();
        var nextNegativeAt = (float[])negativeEvery.Clone();

        for (var epoch = 1; epoch <= epochs; epoch++)
        {
            ct.ThrowIfCancellationRequested();
            var alpha = learningRate * (1 - (float)(epoch - 1) / epochs);

            for (var e = 0; e < edges.Count; e++)
            {
                if (nextAt[e] > epoch)
                    continue;

                var (from, to, _) = edges[e];

                var dx = x[from] - x[to];
                var dy = y[from] - y[to];
                var squared = dx * dx + dy * dy;

                if (squared > 0)
                {
                    var power = MathF.Pow(squared, CurveB);
                    var attract = -2 * CurveA * CurveB * power / squared / (CurveA * power + 1);

                    var gx = Clip(attract * dx) * alpha;
                    var gy = Clip(attract * dy) * alpha;
                    x[from] += gx;
                    y[from] += gy;
                    x[to] -= gx;
                    y[to] -= gy;
                }

                nextAt[e] += every[e];

                var negatives = (int)((epoch - nextNegativeAt[e]) / negativeEvery[e]);

                for (var sample = 0; sample < negatives; sample++)
                {
                    var other = random.Next(count);
                    if (other == from)
                        continue;

                    dx = x[from] - x[other];
                    dy = y[from] - y[other];
                    squared = dx * dx + dy * dy;

                    if (squared > 0)
                    {
                        var repel = 2 * CurveB / ((0.001f + squared) * (CurveA * MathF.Pow(squared, CurveB) + 1));
                        x[from] += Clip(repel * dx) * alpha;
                        y[from] += Clip(repel * dy) * alpha;
                    }
                    else
                    {
                        x[from] += GradientClip * alpha * 0.01f;
                    }
                }

                nextNegativeAt[e] += negatives * negativeEvery[e];
            }
        }
    }

    private static float Clip(float value) => Math.Clamp(value, -GradientClip, GradientClip);

    private static void Rescale(float[] x, float[] y, float spread)
    {
        var reach = MathF.Max(MathF.Abs(TensorPrimitives.MaxMagnitude(x)), MathF.Abs(TensorPrimitives.MaxMagnitude(y)));
        if (reach < 1e-9f)
            return;

        TensorPrimitives.Multiply(x, spread / reach, x);
        TensorPrimitives.Multiply(y, spread / reach, y);
    }

    // В квадрат [0, 1] с общим масштабом осей и полями; одиночные выбросы не сжимают всю карту
    // в точку — границы берутся по 0.5 и 99.5 перцентилям, а выбросы прижимаются к краю.
    private static void Normalize(float[] x, float[] y)
    {
        var (minX, maxX) = Bounds(x);
        var (minY, maxY) = Bounds(y);
        var span = MathF.Max(MathF.Max(maxX - minX, maxY - minY), 1e-6f);

        const float margin = 0.04f;
        var scale = (1 - 2 * margin) / span;
        var offsetX = margin + ((1 - 2 * margin) - (maxX - minX) * scale) / 2;
        var offsetY = margin + ((1 - 2 * margin) - (maxY - minY) * scale) / 2;

        for (var i = 0; i < x.Length; i++)
        {
            x[i] = Math.Clamp(offsetX + (x[i] - minX) * scale, 0, 1);
            y[i] = Math.Clamp(offsetY + (y[i] - minY) * scale, 0, 1);
        }
    }

    private static (float Min, float Max) Bounds(float[] values)
    {
        var sorted = (float[])values.Clone();
        Array.Sort(sorted);

        var low = sorted[(int)(sorted.Length * 0.005)];
        var high = sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.995))];

        return (low, high);
    }
}
