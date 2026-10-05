// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Numerics.Tensors;

namespace App.Recommendations.Embeddings;

public readonly record struct TastePull(Guid TrackId, double Weight);

public sealed record TasteMode(float[] Centre, double Share);

// Вкус в пространстве звучания — один или несколько центров. У слушателя двух непохожих жанров
// единственный средний вектор попадает между ними, где нет ни того, ни другого, а слабый вкус
// целиком уходит в «разведку». Поэтому затравки кластеризуются, и близость к вкусу — это близость
// к ближайшему из центров.
public sealed class TasteModel
{
    public const int MaxModes = 3;

    // Новый центр должен поднять среднюю близость затравок к своему центру хотя бы на столько
    // и забрать не меньше этой доли веса: иначе однородный вкус дробится на почти одинаковые центры.
    private const double MinimumGain = 0.04;
    private const double MinimumShare = 0.15;

    // Отталкивание от нелюбимого не сильнее этой доли притяжения центра.
    private const double MaximumRepulsion = 0.5;

    private const int Iterations = 12;

    public static TasteModel Empty { get; } = new([]);

    public IReadOnlyList<TasteMode> Modes { get; }

    public bool IsEmpty => Modes.Count == 0;

    public int Dimension => IsEmpty ? 0 : Modes[0].Centre.Length;

    private TasteModel(IReadOnlyList<TasteMode> modes) => Modes = modes;

    public static TasteModel Single(float[] centre)
    {
        var copy = centre.ToArray();
        VectorMath.NormalizeInPlace(copy);

        return new TasteModel([new TasteMode(copy, 1)]);
    }

    // Близость каждого трека библиотеки к ближайшему центру вкуса.
    public float[] SimilaritiesIn(EmbeddingSnapshot snapshot)
    {
        if (IsEmpty || Dimension != snapshot.Dimension)
            return new float[snapshot.Count];

        var best = snapshot.SimilaritiesTo(Modes[0].Centre);

        foreach (var mode in Modes.Skip(1))
            TensorPrimitives.Max(best, snapshot.SimilaritiesTo(mode.Centre), best);

        return best;
    }

    public int NearestMode(ReadOnlySpan<float> vector)
    {
        var nearest = -1;
        var best = float.NegativeInfinity;

        for (var index = 0; index < Modes.Count; index++)
        {
            if (vector.Length != Modes[index].Centre.Length)
                continue;

            var similarity = TensorPrimitives.Dot(vector, Modes[index].Centre);
            if (similarity <= best)
                continue;

            best = similarity;
            nearest = index;
        }

        return nearest;
    }

    public static TasteModel Fit(EmbeddingSnapshot snapshot, IEnumerable<TastePull> pulls)
    {
        if (snapshot.IsEmpty)
            return Empty;

        var positives = new List<(int Row, double Weight)>();
        var negatives = new List<(int Row, double Weight)>();

        foreach (var pull in pulls)
        {
            var row = snapshot.RowOf(pull.TrackId);
            if (row < 0 || pull.Weight == 0)
                continue;

            (pull.Weight > 0 ? positives : negatives).Add((row, pull.Weight));
        }

        if (positives.Count == 0)
            return Empty;

        var best = Cluster(snapshot, positives, 1)!;

        for (var k = 2; k <= Math.Min(MaxModes, positives.Count); k++)
        {
            var split = Cluster(snapshot, positives, k);
            if (split is null || split.SmallestShare < MinimumShare || split.Cohesion - best.Cohesion < MinimumGain)
                break;

            best = split;
        }

        var total = positives.Sum(pull => pull.Weight);
        var modes = new List<TasteMode>(best.Centres.Count);

        var repulsion = best.Centres.Select(_ => new float[snapshot.Dimension]).ToList();
        var repelled = new double[best.Centres.Count];

        foreach (var (row, weight) in negatives)
        {
            var vector = snapshot.Vector(row);
            var nearest = Nearest(best.Centres, vector);

            TensorPrimitives.MultiplyAdd(vector, (float)weight, repulsion[nearest], repulsion[nearest]);
            repelled[nearest] += -weight;
        }

        for (var index = 0; index < best.Centres.Count; index++)
        {
            var centre = new float[snapshot.Dimension];

            foreach (var (row, weight) in positives.Where((_, member) => best.Assignment[member] == index))
                TensorPrimitives.MultiplyAdd(snapshot.Vector(row), (float)weight, centre, centre);

            if (repelled[index] > 0)
            {
                var scale = Math.Min(1, MaximumRepulsion * best.Mass[index] / repelled[index]);
                TensorPrimitives.MultiplyAdd(repulsion[index], (float)scale, centre, centre);
            }

            VectorMath.NormalizeInPlace(centre);
            modes.Add(new TasteMode(centre, best.Mass[index] / total));
        }

        return new TasteModel([.. modes.OrderByDescending(mode => mode.Share)]);
    }

    private sealed record Clustering(
        IReadOnlyList<float[]> Centres,
        int[] Assignment,
        double[] Mass,
        double Cohesion)
    {
        public double SmallestShare => Mass.Min() / Mass.Sum();
    }

    // Взвешенный сферический k-means с детерминированным стартом: первый центр — самая сильная
    // затравка, каждый следующий — затравка, дальше всех отстоящая от уже выбранных (с учётом веса).
    private static Clustering? Cluster(EmbeddingSnapshot snapshot, List<(int Row, double Weight)> points, int k)
    {
        var dimension = snapshot.Dimension;
        var centres = new List<float[]>(k)
        {
            snapshot.Vector(points.MaxBy(point => point.Weight).Row).ToArray(),
        };

        while (centres.Count < k)
        {
            var farthest = points
                .Select(point => (point.Row, Distance: point.Weight * (1 - centres.Max(centre =>
                    TensorPrimitives.Dot(snapshot.Vector(point.Row), centre)))))
                .MaxBy(point => point.Distance);

            if (farthest.Distance <= 0)
                return null;

            centres.Add(snapshot.Vector(farthest.Row).ToArray());
        }

        var assignment = new int[points.Count];
        var mass = new double[k];

        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var changed = false;

            for (var index = 0; index < points.Count; index++)
            {
                var nearest = Nearest(centres, snapshot.Vector(points[index].Row));
                changed |= nearest != assignment[index] || iteration == 0;
                assignment[index] = nearest;
            }

            if (!changed)
                break;

            Array.Clear(mass);
            var sums = Enumerable.Range(0, k).Select(_ => new float[dimension]).ToList();

            for (var index = 0; index < points.Count; index++)
            {
                var (row, weight) = points[index];
                TensorPrimitives.MultiplyAdd(snapshot.Vector(row), (float)weight, sums[assignment[index]], sums[assignment[index]]);
                mass[assignment[index]] += weight;
            }

            if (mass.Any(value => value <= 0))
                return null;

            foreach (var sum in sums)
                VectorMath.NormalizeInPlace(sum);

            centres = sums;
        }

        var total = points.Sum(point => point.Weight);
        var cohesion = points
            .Select((point, index) => point.Weight * TensorPrimitives.Dot(snapshot.Vector(point.Row), centres[assignment[index]]))
            .Sum() / total;

        return new Clustering(centres, assignment, mass, cohesion);
    }

    private static int Nearest(IReadOnlyList<float[]> centres, ReadOnlySpan<float> vector)
    {
        var nearest = 0;
        var best = float.NegativeInfinity;

        for (var index = 0; index < centres.Count; index++)
        {
            var similarity = TensorPrimitives.Dot(vector, centres[index]);
            if (similarity <= best)
                continue;

            best = similarity;
            nearest = index;
        }

        return nearest;
    }
}
