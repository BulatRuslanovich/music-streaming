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
        int count) =>
        history
            .Where(pair => Trustworthy(pair.Value))
            .Select(pair => new RecommendationSeed(pair.Key, WeightOf(pair.Value, now)))
            .Where(seed => seed.Weight > 0)
            .OrderByDescending(seed => seed.Weight)
            .ThenBy(seed => seed.TrackId)
            .Take(Math.Max(0, count))
            .ToList();

    /// <summary>
    /// Отсев недоверия. Все три условия обязаны совпасть: трек, который дважды бросили почти
    /// сразу и который так и не набрал веса, скорее случайное нажатие, чем вкус. Любое одно из
    /// трёх по отдельности — нормальная жизнь: бросают и любимое, и у свежего трека вес мал.
    /// </summary>
    private static bool Trustworthy(TrackHistory history) =>
        history.Score > 0
        && !(history.SkipCount >= 2 && history.AverageCompletion < 0.20 && history.Score < 0.35);

    /// <summary>
    /// Вес сида: сколько трек значит и насколько он свежий в памяти.
    /// </summary>
    /// <remarks>
    /// Три множителя, и у каждого свой смысл.
    /// <para>
    /// <b>Вовлечённость</b> — не среднее, а максимум: достаточно одного явного жеста. Лестница
    /// 0.85 / 0.95 / 1.0 упорядочивает их по силе намерения — дослушал слабее, чем переслушал,
    /// переслушал слабее, чем положил в плейлист. Вес профиля удваивается и зажимается, чтобы
    /// давно любимый трек не проигрывал недавнему только из-за отсутствия этих жестов.
    /// </para>
    /// <para>
    /// <b>Повторы</b> насыщаются: <c>1 − exp(−plays/3)</c> даёт ~63 % на третьем прослушивании и
    /// почти единицу на десятом. Разница между одним и тремя прослушиваниями значима, между
    /// тридцатью и сорока — нет.
    /// </para>
    /// <para>
    /// <b>Оба множителя аффинны</b> (0.35 + 0.65·x и 0.45 + 0.55·x), то есть ни один не может
    /// обнулить вес. Свободный член — это «сколько трек стоит, даже если по этой оси он пуст»:
    /// сид без жестов и месячной давности всё ещё сид, просто слабее втрое, а не в бесконечность.
    /// </para>
    /// </remarks>
    private static double WeightOf(TrackHistory history, DateTimeOffset now)
    {
        var engagement = Math.Max(
            Math.Clamp(history.AverageCompletion, 0, 1),
            Math.Max(
                history.CompletedCount > 0 ? 0.85 : 0,
                Math.Max(history.ReplayCount > 0 ? 0.95 : 0, history.PlaylistAdds > 0 ? 1 : 0)));

        engagement = Math.Max(engagement, Math.Clamp(history.Score * 2, 0, 1));

        var repetition = 1 - Math.Exp(-Math.Max(1, history.PlayCount) / 3.0);
        var age = Math.Max(0, (now - history.LastPlayedAt).TotalSeconds);
        var recency = Math.Pow(0.5, age / RecencyHalfLife.TotalSeconds);

        return history.Score
               * (0.35 + 0.45 * engagement + 0.20 * repetition)
               * (0.45 + 0.55 * recency);
    }
}
