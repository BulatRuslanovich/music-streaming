// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Recommendations;
using MusicStreaming.Application.Recommendations.Scoring;

namespace MusicStreaming.Application.Services.Recommendations;

/// <summary>
/// Picks the tracks a recommendation run is allowed to reason from.
/// </summary>
/// <remarks>
/// Сид — это трек, от которого пляшет всё остальное: соседи, «похоже на», якорь радио. Ошибиться
/// здесь дороже, чем в любом весе ниже по конвейеру, поэтому отбор двухступенчатый: сначала
/// отсеиваются треки, которым нельзя верить, и только потом оставшиеся взвешиваются.
/// </remarks>
internal static class RecommendationSeedSelector
{
    private static readonly TimeSpan RecencyHalfLife = TimeSpan.FromDays(30);

    public static List<RecommendationSeed> Select(
        IReadOnlyDictionary<Guid, TrackHistory> history,
        DateTimeOffset now,
        int count)
    {
        var seeds = new List<RecommendationSeed>();

        foreach (var (trackId, track) in history)
        {
            // Отсев недоверия. Все три условия обязаны совпасть: трек, который дважды бросили почти
            // сразу и который так и не набрал веса, скорее случайное нажатие, чем вкус. Любое одно
            // из трёх по отдельности — нормальная жизнь: бросают и любимое, и у свежего трека вес мал.
            if (track.Score <= 0 || (track.SkipCount >= 2 && track.AverageCompletion < 0.20 && track.Score < 0.35))
                continue;

            // Вес сида — сколько трек значит и насколько он свежий в памяти, три множителя.
            //
            // Вовлечённость — не среднее, а максимум: достаточно одного явного жеста. Лестница
            // 0.85 / 0.95 / 1.0 упорядочивает их по силе намерения — дослушал слабее, чем переслушал,
            // переслушал слабее, чем положил в плейлист. Вес профиля удваивается и зажимается, чтобы
            // давно любимый трек не проигрывал недавнему только из-за отсутствия этих жестов.
            var engagement = Math.Max(
                Math.Clamp(track.AverageCompletion, 0, 1),
                Math.Max(
                    track.CompletedCount > 0 ? 0.85 : 0,
                    Math.Max(track.ReplayCount > 0 ? 0.95 : 0, track.PlaylistAdds > 0 ? 1 : 0)));

            engagement = Math.Max(engagement, Math.Clamp(track.Score * 2, 0, 1));

            // Повторы насыщаются: 1 − exp(−plays/3) даёт ~63 % на третьем прослушивании и почти
            // единицу на десятом. Разница между одним и тремя прослушиваниями значима, между
            // тридцатью и сорока — нет.
            var repetition = 1 - Math.Exp(-Math.Max(1, track.PlayCount) / 3.0);
            var age = Math.Max(0, (now - track.LastPlayedAt).TotalSeconds);
            var recency = Math.Pow(0.5, age / RecencyHalfLife.TotalSeconds);

            // Оба множителя аффинны (0.35 + 0.65·x и 0.45 + 0.55·x), то есть ни один не может
            // обнулить вес. Свободный член — это «сколько трек стоит, даже если по этой оси он пуст»:
            // сид без жестов и месячной давности всё ещё сид, просто слабее втрое, а не в бесконечность.
            var weight = track.Score
                         * (0.35 + 0.45 * engagement + 0.20 * repetition)
                         * (0.45 + 0.55 * recency);

            if (weight > 0)
                seeds.Add(new RecommendationSeed(trackId, weight));
        }

        return seeds
            .OrderByDescending(seed => seed.Weight)
            .ThenBy(seed => seed.TrackId)
            .Take(Math.Max(0, count))
            .ToList();
    }
}
