// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations.Scoring;

/// <summary>Насколько вектору вкуса уже можно доверять.</summary>
public enum VectorMaturityLevel
{
    /// <summary>Сигналов почти нет: очередь идёт в режиме знакомства, почти наугад.</summary>
    Discovering = 0,

    Forming = 1,
    Ready = 2,
}

/// <summary>
/// Зрелость <i>вектора</i>, отдельная от <see cref="Domain.Entities.Recommendations.ProfileMaturity"/>.
/// <para>
/// Это не дублирование: скалярный профиль меряет всю историю и решает, какой набор весов взять,
/// а вектор меряет только то, что впитал сам, и решает долю exploration. Слушатель с полусотней
/// сигналов по трекам, у которых ещё нет эмбеддинга, зрелый по профилю и только начинающий
/// по вектору — и это ровно то, что нужно знать очереди.
/// </para>
/// </summary>
public static class VectorMaturity
{
    public static VectorMaturityLevel Of(int positiveCount, int formingAt, int readyAt)
    {
        if (positiveCount >= readyAt)
            return VectorMaturityLevel.Ready;

        return positiveCount >= formingAt ? VectorMaturityLevel.Forming : VectorMaturityLevel.Discovering;
    }

    /// <summary>
    /// Доля exploration по зрелости: в начале исследуем много, к зрелости опускаемся до базовой.
    /// <paramref name="discover"/> берётся максимумом, чтобы настройка с discover меньше base
    /// не делала новичка консервативнее опытного слушателя.
    /// </summary>
    public static double EffectiveExplore(double baseRatio, double discover, VectorMaturityLevel maturity) =>
        maturity switch
        {
            VectorMaturityLevel.Discovering => Math.Max(discover, baseRatio),
            VectorMaturityLevel.Forming => (baseRatio + discover) / 2,
            _ => baseRatio,
        };
}
