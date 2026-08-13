namespace MusicStreaming.Application.Recommendations.Scoring;

/// <summary>
/// Экспоненциальное затухание по свежести, применяемое инкрементально.
///
/// <para>
/// Вкус меняется. То, что человек слушал каждый день прошлой весной, не должно перевешивать то,
/// что он слушает на этой неделе, — поэтому каждый вес угасает с периодом полураспада. Пересчёт
/// этого из потока событий означал бы хранить все события вечно и перечитывать их при каждом
/// роллапе; вместо этого аккумулятор хранит уже затухшую сумму вместе с моментом, на который она
/// актуальна. Каждое новое событие стоит O(1), а старые события можно удалять.
/// </para>
/// </summary>
public static class RecencyDecay
{
    /// <summary>Множитель для веса возрастом <paramref name="age"/>.</summary>
    /// <param name="age">Возраст веса — время, прошедшее с момента наблюдения.</param>
    /// <param name="halfLifeDays">Период полураспада в днях: за это время множитель уменьшается вдвое.</param>
    /// <returns>Множитель в (0, 1]: 1 для события «прямо сейчас» или из будущего, экспоненциально убывает с возрастом.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="halfLifeDays"/> не положителен.</exception>
    public static double Factor(TimeSpan age, double halfLifeDays)
    {
        if (halfLifeDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(halfLifeDays), "Период полураспада должен быть положительным.");

        // Устройство со спешащими часами присылает события из будущего; считаем их за «только что»,
        // вместо того чтобы позволить отрицательному возрасту усилить вес.
        var days = age.TotalDays;
        if (days <= 0)
            return 1.0;

        return Math.Pow(2, -days / halfLifeDays);
    }

    /// <summary>
    /// Вносит <paramref name="addedWeight"/>, наблюдённый в момент <paramref name="at"/>, в
    /// аккумулятор, актуальный на <paramref name="anchor"/>.
    ///
    /// <para>
    /// События вне порядка обрабатываются без сдвига якоря назад: старое событие вместо этого
    /// затухает вперёд до текущего якоря. Поэтому результат не зависит от порядка обработки — а это
    /// важно, потому что клиенты батчат и повторяют отправку.
    /// </para>
    /// </summary>
    /// <param name="weight">Текущее значение аккумулятора, актуальное на момент <paramref name="anchor"/>.</param>
    /// <param name="anchor">Момент, на который актуально текущее значение <paramref name="weight"/>.</param>
    /// <param name="addedWeight">Вклад нового события, ещё не учтённый в аккумуляторе.</param>
    /// <param name="at">Момент, когда произошло новое событие.</param>
    /// <param name="halfLifeDays">Период полураспада в днях, применяемый к обеим сторонам расчёта.</param>
    /// <returns>Новое значение аккумулятора и новый якорь — либо <paramref name="at"/>, если событие свежее текущего якоря, либо прежний <paramref name="anchor"/>.</returns>
    public static (double Weight, DateTimeOffset Anchor) Accumulate(
        double weight,
        DateTimeOffset anchor,
        double addedWeight,
        DateTimeOffset at,
        double halfLifeDays)
    {
        if (at >= anchor)
            return (weight * Factor(at - anchor, halfLifeDays) + addedWeight, at);

        return (weight + addedWeight * Factor(anchor - at, halfLifeDays), anchor);
    }

    /// <summary>Значение аккумулятора на момент <paramref name="now"/>.</summary>
    /// <param name="weight">Значение аккумулятора, актуальное на <paramref name="anchor"/>.</param>
    /// <param name="anchor">Момент последнего обновления аккумулятора.</param>
    /// <param name="now">Момент, на который нужно узнать значение — обычно текущее время чтения профиля.</param>
    /// <param name="halfLifeDays">Период полураспада в днях.</param>
    public static double ValueAt(double weight, DateTimeOffset anchor, DateTimeOffset now, double halfLifeDays) =>
        weight * Factor(now - anchor, halfLifeDays);
}
