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
    public static double Shrink(double value, int support, double lambda)
    {
        if (support <= 0)
            return 0;

        return value * (support / (support + lambda));
    }

    /// <summary>Свежесть объекта, убывающая от 1 до 0 за <paramref name="windowDays"/> дней.</summary>
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
