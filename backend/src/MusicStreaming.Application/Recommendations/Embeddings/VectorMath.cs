// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Numerics.Tensors;

namespace MusicStreaming.Application.Recommendations.Embeddings;

/// <summary>
/// Операции над векторами эмбеддингов. Все вектора в индексе единичной длины, поэтому скалярное
/// произведение и есть косинус — отдельной функции косинуса здесь намеренно нет.
/// </summary>
public static class VectorMath
{
    /// <summary>Ниже этой нормы вектор считается вырожденным и остаётся как есть.</summary>
    private const float DegenerateNorm = 1e-12f;

    public static void NormalizeInPlace(Span<float> vector)
    {
        var norm = TensorPrimitives.Norm(vector);
        if (norm < DegenerateNorm)
            return;

        TensorPrimitives.Divide(vector, norm, vector);
    }

    public static float[] Normalized(ReadOnlySpan<float> vector)
    {
        var copy = vector.ToArray();
        NormalizeInPlace(copy);
        return copy;
    }

    public static float Dot(ReadOnlySpan<float> left, ReadOnlySpan<float> right) =>
        TensorPrimitives.Dot(left, right);

    /// <summary>
    /// Нормированная выпуклая смесь. Используется для запроса «вкус сейчас»:
    /// normalize(0.7 * global + 0.3 * daypart).
    /// </summary>
    public static float[] Blend(ReadOnlySpan<float> left, float leftWeight, ReadOnlySpan<float> right, float rightWeight)
    {
        if (left.IsEmpty)
            return right.IsEmpty ? [] : Normalized(right);

        if (right.IsEmpty || left.Length != right.Length)
            return Normalized(left);

        var result = new float[left.Length];
        for (var i = 0; i < result.Length; i++)
            result[i] = left[i] * leftWeight + right[i] * rightWeight;

        NormalizeInPlace(result);
        return result;
    }

    /// <summary>Нормированное среднее набора векторов. Пустой набор даёт пустой вектор.</summary>
    public static float[] Mean(IReadOnlyList<float[]> vectors, int dimension)
    {
        if (vectors.Count == 0 || dimension <= 0)
            return [];

        // Накопление в double: на 50k слагаемых float теряет значащие разряды.
        var accumulator = new double[dimension];
        var counted = 0;

        foreach (var vector in vectors)
        {
            if (vector.Length != dimension)
                continue;

            for (var i = 0; i < dimension; i++)
                accumulator[i] += vector[i];

            counted++;
        }

        if (counted == 0)
            return [];

        var result = new float[dimension];
        for (var i = 0; i < dimension; i++)
            result[i] = (float)(accumulator[i] / counted);

        NormalizeInPlace(result);
        return result;
    }

    /// <summary>
    /// Квантиль с линейной интерполяцией — семёрка по классификации Хиндмана–Фэна, то же, что
    /// делает numpy по умолчанию. Порт musik: по нему определяется граница far-пула.
    /// Входной спан не изменяется.
    /// </summary>
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
