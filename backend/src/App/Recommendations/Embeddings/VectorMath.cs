// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Numerics.Tensors;

namespace App.Recommendations.Embeddings;

public static class VectorMath
{
    private const float DegenerateNorm = 1e-12f;

    public static void NormalizeInPlace(Span<float> vector)
    {
        var norm = TensorPrimitives.Norm(vector);
        if (norm < DegenerateNorm)
            return;

        TensorPrimitives.Divide(vector, norm, vector);
    }

    // Доля значений, которые каждое обходит: 0 у наименьшего, 1 у наибольшего.
    public static float[] PercentileRanks(ReadOnlySpan<float> values)
    {
        var ranks = new float[values.Length];
        if (values.Length == 1)
        {
            ranks[0] = 1;
            return ranks;
        }

        var keys = values.ToArray();
        var order = Enumerable.Range(0, values.Length).ToArray();
        Array.Sort(keys, order);

        for (var rank = 0; rank < order.Length; rank++)
            ranks[order[rank]] = rank / (float)(order.Length - 1);

        return ranks;
    }

    public static float Quantile(ReadOnlySpan<float> values, double q)
    {
        if (values.IsEmpty)
            return 0f;

        if (values.Length == 1)
            return values[0];

        var sorted = values.ToArray();
        Array.Sort(sorted);

        var position = Math.Clamp(q, 0, 1) * (sorted.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);

        if (lower == upper)
            return sorted[lower];

        var fraction = (float)(position - lower);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
    }
}
