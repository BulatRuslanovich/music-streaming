namespace MusicStreaming.Application.Recommendations.Scoring;

/// <summary>
/// Смесь сигналов, по которой оценивается кандидат. Отдельный набор на каждую зрелость профиля:
/// слушатель, о котором движок ничего не знает, ранжируется почти целиком по популярности и
/// новизне, а по мере накопления свидетельств верх берут персональные сигналы.
///
/// <para>
/// Изменяемый класс, привязываемый к конфигурации, чтобы баланс можно было перенастроить на
/// работающей установке без пересборки.
/// </para>
/// </summary>
public class RankingWeights
{
    /// <summary>Похожесть метаданных на то, что пользователь уже слушает.</summary>
    public double Content { get; set; }

    /// <summary>Совстречаемость с тем, что пользователь слушает, — коллаборативный сигнал.</summary>
    public double Collaborative { get; set; }

    /// <summary>Прямое аффинити к исполнителю и жанру кандидата.</summary>
    public double Behavior { get; set; }

    /// <summary>Насколько это слушает библиотека в целом.</summary>
    public double Popularity { get; set; }

    /// <summary>Насколько недавно это добавлено.</summary>
    public double Freshness { get; set; }

    /// <summary>
    /// Охват жанров библиотеки. Используется только на холодном старте: когда персонализировать
    /// не по чему, первая страница, показывающая срез библиотеки, лучше той, что повторяет её
    /// самый крупный жанр.
    /// </summary>
    public double Coverage { get; set; }

    public double Total => Content + Collaborative + Behavior + Popularity + Freshness + Coverage;

    /// <summary>Профиля ещё нет — опираемся на библиотеку, а не на слушателя.</summary>
    public static RankingWeights ColdDefaults() => new()
    {
        Popularity = 0.40,
        Freshness = 0.25,
        Coverage = 0.35,
    };

    /// <summary>Истории хватает, чтобы понять форму вкуса, но не чтобы верить совстречаемости.</summary>
    public static RankingWeights WarmDefaults() => new()
    {
        Content = 0.40,
        Collaborative = 0.15,
        Behavior = 0.20,
        Popularity = 0.15,
        Freshness = 0.10,
    };

    /// <summary>Истории достаточно, чтобы совстречаемость несла наибольший вес.</summary>
    public static RankingWeights MatureDefaults() => new()
    {
        Content = 0.25,
        Collaborative = 0.30,
        Behavior = 0.25,
        Popularity = 0.10,
        Freshness = 0.10,
    };

    /// <summary>
    /// Складывает компоненты. Каждый лежит в [0, 1], кроме поведенческого — он в [-1, 1], чтобы
    /// нелюбимый исполнитель активно тянул кандидата вниз, а не просто не поднимал его.
    /// </summary>
    public double Combine(
        double content,
        double collaborative,
        double behavior,
        double popularity,
        double freshness,
        double coverage) =>
        Content * content
        + Collaborative * collaborative
        + Behavior * behavior
        + Popularity * popularity
        + Freshness * freshness
        + Coverage * coverage;
}
