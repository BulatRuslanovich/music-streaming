// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Numerics.Tensors;
using MusicStreaming.Application.Recommendations.Embeddings;

namespace MusicStreaming.UnitTests;

internal static class Vectors
{
    public static float[] Unit(params float[] values)
    {
        var copy = values.ToArray();
        VectorMath.NormalizeInPlace(copy);
        return copy;
    }

    public static float Dot(ReadOnlySpan<float> left, ReadOnlySpan<float> right) => TensorPrimitives.Dot(left, right);
}
