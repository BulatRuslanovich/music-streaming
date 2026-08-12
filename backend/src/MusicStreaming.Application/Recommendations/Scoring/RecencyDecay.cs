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
    public static double ValueAt(double weight, DateTimeOffset anchor, DateTimeOffset now, double halfLifeDays) =>
        weight * Factor(now - anchor, halfLifeDays);
}
