// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Numerics;

namespace MusicStreaming.Infrastructure.Audio;

/// <summary>Быстрое преобразование Фурье на месте, radix-2. Длина обязана быть степенью двойки.</summary>
internal static class Fft
{
    public static void Transform(Complex[] values)
    {
        for (int index = 1, reversed = 0; index < values.Length; index++)
        {
            var bit = values.Length >> 1;
            for (; (reversed & bit) != 0; bit >>= 1)
                reversed ^= bit;
            reversed ^= bit;

            if (index < reversed)
                (values[index], values[reversed]) = (values[reversed], values[index]);
        }

        for (var length = 2; length <= values.Length; length <<= 1)
        {
            var angle = -2 * Math.PI / length;
            var step = new Complex(Math.Cos(angle), Math.Sin(angle));

            for (var offset = 0; offset < values.Length; offset += length)
            {
                var rotation = Complex.One;
                for (var index = 0; index < length / 2; index++)
                {
                    var even = values[offset + index];
                    var odd = values[offset + index + length / 2] * rotation;
                    values[offset + index] = even + odd;
                    values[offset + index + length / 2] = even - odd;
                    rotation *= step;
                }
            }
        }
    }
}
