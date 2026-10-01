// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Recommendations.Embeddings;

public static class TasteVectorMath
{
    public static float[] Fold(
        ReadOnlySpan<float> vector,
        ReadOnlySpan<float> trackVector,
        double signedWeight,
        double alpha)
    {
        if (trackVector.IsEmpty || alpha <= 0 || signedWeight == 0)
            return vector.ToArray();

        if (vector.IsEmpty || vector.Length != trackVector.Length)
        {
            var seeded = trackVector.ToArray();

            if (signedWeight < 0)
            {
                for (var i = 0; i < seeded.Length; i++)
                    seeded[i] = -seeded[i];
            }

            VectorMath.NormalizeInPlace(seeded);
            return seeded;
        }

        var keep = (float)(1 - alpha);
        var pull = (float)(alpha * signedWeight);

        var result = new float[vector.Length];
        for (var i = 0; i < result.Length; i++)
            result[i] = keep * vector[i] + pull * trackVector[i];

        VectorMath.NormalizeInPlace(result);
        return result;
    }
}
