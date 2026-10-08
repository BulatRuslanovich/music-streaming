// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace Infrastructure.Audio;

// Какие 10-секундные куски трека слушает CLAP (его вход — ровно 10 с). Окна идут равномерно по телу трека:
// вступление и концовка — самые нетипичные места (тихое интро, затухание), поэтому края в 5 % пропускаются,
// а на каждые ~30 с трека приходится одно окно — от 3 до 8. Вектор трека — среднее по окнам.
public static class ClapWindowPlanner
{
    public const double WindowSeconds = 10.0;

    // Смена стратегии сама запускает фоновый пересчёт библиотеки (AudioEmbeddingWorker).
    public const string Strategy = "clap_body_v2";

    public const int MinimumWindows = 3;

    public const int MaximumWindows = 8;

    private const double SecondsPerWindow = 30.0;

    private const double EdgeShare = 0.05;

    private const double MaximumEdgeSeconds = 15.0;

    private const double MinimumSeparationSeconds = 0.5;

    private const double ShortTrackSlackSeconds = 0.05;

    public static IReadOnlyList<double> Plan(double durationSeconds)
    {
        if (durationSeconds <= 0)
            return [];

        if (durationSeconds <= WindowSeconds + ShortTrackSlackSeconds)
            return [0.0];

        var count = Math.Clamp((int)Math.Round(durationSeconds / SecondsPerWindow), MinimumWindows, MaximumWindows);

        var edge = Math.Min(durationSeconds * EdgeShare, MaximumEdgeSeconds);
        var first = edge;
        var last = durationSeconds - WindowSeconds - edge;

        // Короткому треку края не по карману: тогда окна от самого начала до самого конца.
        if (last <= first)
        {
            first = 0;
            last = durationSeconds - WindowSeconds;
        }

        var offsets = new List<double>(count);

        for (var index = 0; index < count; index++)
        {
            var offset = first + (last - first) * index / (count - 1);

            if (offsets.TrueForAll(kept => Math.Abs(offset - kept) >= MinimumSeparationSeconds))
                offsets.Add(offset);
        }

        return offsets;
    }
}
