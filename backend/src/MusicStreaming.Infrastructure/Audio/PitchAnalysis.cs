// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Infrastructure.Audio;

/// <summary>
/// Раскладка спектра по высоте: мел-полосы для тембра и свёртка в хрому для оценки тональности.
/// </summary>
internal static class PitchAnalysis
{
    public static double[] MelEdges(double lowHz, double highHz, int bands)
    {
        var low = ToMel(lowHz);
        var high = ToMel(Math.Max(highHz, lowHz + 1));
        var edges = new double[bands + 1];

        for (var index = 0; index <= bands; index++)
            edges[index] = ToHz(low + (high - low) * index / bands);

        return edges;
    }

    public static int BandOf(double[] edges, double hz)
    {
        if (hz <= edges[0])
            return 0;

        for (var band = 1; band < edges.Length - 1; band++)
        {
            if (hz < edges[band])
                return band - 1;
        }

        return edges.Length - 2;
    }

    // Ниже 130 Гц разрешение кадра меньше полутона, а выше 2 кГц у пятой гармоники уже нет
    // отношения к основному тону — за этими границами свёртка в хрому только шумит.
    public static void Fold(double[] chroma, double hz, double magnitude)
    {
        if (hz is < 130 or > 2000 || magnitude <= 0)
            return;

        var semitone = 12 * Math.Log2(hz / 440.0) + 69;
        var pitchClass = (int)Math.Round(semitone) % 12;

        if (pitchClass < 0)
            pitchClass += 12;

        chroma[pitchClass] += magnitude;
    }

    // Профили Крумхансл — Шмуклера: корреляция хромы с каждым из 24 поворотов.
    private static readonly double[] MajorProfile =
        [6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88];

    private static readonly double[] MinorProfile =
        [6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17];

    public static (int? Key, bool IsMinor, double Strength) Key(double[] chroma)
    {
        var total = chroma.Sum();
        if (total <= 1e-9)
            return (null, false, 0);

        var best = double.NegativeInfinity;
        var bestKey = 0;
        var bestMinor = false;

        for (var root = 0; root < 12; root++)
        {
            var major = Correlate(chroma, MajorProfile, root);
            var minor = Correlate(chroma, MinorProfile, root);

            if (major > best)
                (best, bestKey, bestMinor) = (major, root, false);

            if (minor > best)
                (best, bestKey, bestMinor) = (minor, root, true);
        }

        return best <= 0 ? (null, false, 0) : (bestKey, bestMinor, Math.Clamp(best, 0, 1));
    }

    private static double Correlate(double[] chroma, double[] profile, int root)
    {
        var chromaMean = chroma.Average();
        var profileMean = profile.Average();

        var product = 0.0;
        var left = 0.0;
        var right = 0.0;

        for (var index = 0; index < 12; index++)
        {
            var a = chroma[(index + root) % 12] - chromaMean;
            var b = profile[index] - profileMean;

            product += a * b;
            left += a * a;
            right += b * b;
        }

        return product / Math.Sqrt(Math.Max(1e-12, left * right));
    }

    private static double ToMel(double hz) => 2595 * Math.Log10(1 + hz / 700);

    private static double ToHz(double mel) => 700 * (Math.Pow(10, mel / 2595) - 1);
}
