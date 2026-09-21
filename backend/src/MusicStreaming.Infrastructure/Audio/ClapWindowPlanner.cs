// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Infrastructure.Audio;

/// <summary>
/// Какие участки трека отдавать модели. Модель принимает ровно 10 секунд, поэтому трек
/// представляется тремя окнами — начало, середина, конец — и их вектора усредняются.
/// <para>
/// musik нарезал по 30 секунд, но это иллюзия: <c>ClapFeatureExtractor</c> в режиме
/// <c>rand_trunc</c> всё равно обрезал каждое окно до <b>случайных</b> 10 секунд несеянным
/// генератором, из-за чего повторный эмбеддинг того же файла давал другой вектор. Явные 10
/// секунд детерминированы и декодируют втрое меньше аудио.
/// </para>
/// </summary>
public static class ClapWindowPlanner
{
    public const double WindowSeconds = 10.0;

    /// <summary>Токен стратегии; вместе с идентификатором модели играет роль версии алгоритма.</summary>
    public const string Strategy = "clap_3x10_v1";

    /// <summary>Ближе этого окна считаются одним и тем же.</summary>
    private const double MinimumSeparationSeconds = 0.5;

    /// <summary>Запас, ниже которого дробить трек на окна незачем.</summary>
    private const double ShortTrackSlackSeconds = 0.05;

    /// <summary>Смещения окон в секундах. Пустой список — брать нечего.</summary>
    public static IReadOnlyList<double> Plan(double durationSeconds)
    {
        if (durationSeconds <= 0)
            return [];

        if (durationSeconds <= WindowSeconds + ShortTrackSlackSeconds)
            return [0.0];

        var offsets = new List<double>(3);

        foreach (var candidate in (double[])
                 [0.0, (durationSeconds - WindowSeconds) / 2, durationSeconds - WindowSeconds])
        {
            var offset = Math.Max(0.0, candidate);

            if (offsets.TrueForAll(kept => Math.Abs(offset - kept) >= MinimumSeparationSeconds))
                offsets.Add(offset);
        }

        return offsets;
    }
}
