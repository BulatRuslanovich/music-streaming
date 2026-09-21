// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations.Embeddings;

/// <summary>
/// Экспоненциальное скользящее среднее вкуса: <c>v ← normalize((1−α)·v + α·w·t)</c>.
/// <para>
/// Знаковый вес <c>w</c> умножает только новое слагаемое, поэтому лайк с |w| = 2 тянет вдвое
/// сильнее обычного прослушивания — это не выпуклая комбинация, и так задумано.
/// </para>
/// </summary>
public static class TasteVectorMath
{
    /// <param name="vector">Текущий вкус; пустой означает холодный старт.</param>
    /// <param name="trackVector">Единичный вектор трека.</param>
    /// <param name="signedWeight">Знаковый вес события; ноль ничего не меняет.</param>
    /// <param name="alpha">Скорость забывания, 0..1.</param>
    /// <returns>Новый нормированный вектор.</returns>
    public static float[] Fold(
        ReadOnlySpan<float> vector,
        ReadOnlySpan<float> trackVector,
        double signedWeight,
        double alpha)
    {
        if (trackVector.IsEmpty || alpha <= 0 || signedWeight == 0)
            return vector.ToArray();

        // Холодный старт: первый же сигнал задаёт направление целиком. Отрицательный вес
        // означает «точно не туда», поэтому вектор разворачивается.
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
