using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Recommendations.Scoring;

/// <summary>
/// Преобразования между сырым накопленным весом и ограниченными оценками, которые сравнивает ранкер.
/// </summary>
public static class AffinityMath
{
    /// <summary>
    /// Сжимает неограниченный вес в интервал (-1, 1).
    ///
    /// <para>
    /// Без этого один навязчиво переслушанный трек оказался бы на порядки выше всего остального, и
    /// каждая полка схлопнулась бы на него. Константа мягкости задаёт, что считать «большим
    /// объёмом свидетельств»: при <c>softness = 3</c> один лайк уже даёт ~0.45, а десять полных
    /// прослушиваний — ~0.77. Сильное предпочтение по-прежнему выигрывает, но не бесконечно.
    /// </para>
    /// </summary>
    /// <param name="weight">Сырой накопленный вес (сумма вкладов событий) — теоретически неограничен в обе стороны.</param>
    /// <param name="softness">Насколько «жёстко» сжимать — чем больше, тем больше событий нужно, чтобы приблизиться к ±1.</param>
    /// <returns>Значение в открытом интервале (-1, 1), пригодное для прямого сравнения между треками.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="softness"/> не положительна.</exception>
    public static double Normalize(double weight, double softness)
    {
        if (softness <= 0)
            throw new ArgumentOutOfRangeException(nameof(softness), "Мягкость должна быть положительной.");

        return weight / (Math.Abs(weight) + softness);
    }

    /// <summary>
    /// Насколько можно доверять профилю, исходя из числа положительных сигналов за ним. Определяет
    /// применяемый набор весов, чтобы слушателю с тремя прослушиваниями не подавали
    /// «персональную» полку, построенную на шуме.
    /// </summary>
    /// <param name="positiveSignals">Число положительных сигналов (лайков, дослушиваний и т.п.), накопленных профилем.</param>
    /// <param name="warmThreshold">Порог, начиная с которого профиль считается «тёплым» — есть хоть какой-то сигнал.</param>
    /// <param name="matureThreshold">Порог, начиная с которого профиль считается «зрелым» — сигнала достаточно для полностью персонализированных весов.</param>
    /// <returns>Ступень зрелости профиля, определяющую, какой набор весов ранжирования применить.</returns>
    public static ProfileMaturity MaturityFor(int positiveSignals, int warmThreshold, int matureThreshold)
    {
        if (positiveSignals >= matureThreshold)
            return ProfileMaturity.Mature;

        return positiveSignals >= warmThreshold ? ProfileMaturity.Warm : ProfileMaturity.Cold;
    }

    /// <summary>
    /// Подтягивает похожесть или долю к нулю, когда за ними мало свидетельств. Одна общая сессия у
    /// двух треков — совпадение; двадцать — закономерность. Без этого разреженные ранние данные
    /// дают уверенную бессмыслицу.
    /// </summary>
    /// <param name="value">Сырое значение (похожесть или доля), которое нужно подтянуть к нулю при слабой поддержке.</param>
    /// <param name="support">Число наблюдений, на которых основано <paramref name="value"/> — например, число общих сессий.</param>
    /// <param name="lambda">Псевдо-счётчик: во сколько наблюдений «весит» априорное недоверие к значению без поддержки.</param>
    /// <returns><paramref name="value"/>, уменьшенное пропорционально нехватке наблюдений; 0, если наблюдений нет вовсе.</returns>
    public static double Shrink(double value, int support, double lambda)
    {
        if (support <= 0)
            return 0;

        return value * (support / (support + lambda));
    }

    /// <summary>Свежесть объекта, убывающая от 1 до 0 за <paramref name="windowDays"/> дней.</summary>
    /// <param name="addedAt">Момент появления объекта (например, добавления трека в библиотеку).</param>
    /// <param name="now">Текущий момент, относительно которого считается возраст.</param>
    /// <param name="windowDays">Длина окна свежести в днях — по его истечении объект считается полностью «не новым».</param>
    /// <returns>1 для только что появившегося объекта, линейно убывает до 0 к концу окна; 0 при неположительном окне.</returns>
    public static double Freshness(DateTimeOffset addedAt, DateTimeOffset now, double windowDays)
    {
        if (windowDays <= 0)
            return 0;

        var age = (now - addedAt).TotalDays;
        if (age <= 0)
            return 1;

        return age >= windowDays ? 0 : 1 - age / windowDays;
    }
}
