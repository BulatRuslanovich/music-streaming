// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations.Scoring;

/// <summary>
/// How much each signal counts when ranking a candidate. One preset per profile maturity.
/// </summary>
/// <remarks>
/// Веса — доли: <see cref="Total"/> обязан быть единицей, это проверяет тест. Поэтому поднять
/// один терм можно только опустив другой, и пресеты читаются как ответ на вопрос «чему мы сейчас
/// верим». У холодного профиля личных сигналов нет вовсе, и вес уходит в популярность и охват;
/// у зрелого — наоборот.
/// <para>
/// Отсутствующий сигнал не обнуляется, а перераспределяется между присутствующими — см.
/// <see cref="Combine"/>. Поэтому трек без эмбеддинга не проигрывает автоматически тому, у кого
/// эмбеддинг есть.
/// </para>
/// </remarks>
public class RankingWeights
{
    /// <summary>
    /// Насколько трек похож на вектор вкуса слушателя. Того, чего у скалярных аффинити нет:
    /// они знают, каких артистов человек любит, но не то, как он звучит.
    /// </summary>
    public double Taste { get; set; }

    /// <summary>Культурное родство по <c>track_similarity</c>: кредиты, альбом, жанр, год, теги.</summary>
    public double Content { get; set; }

    /// <summary>Косинус к сидам — «похоже на то, что вы только что слушали».</summary>
    public double Audio { get; set; }

    /// <summary>«Это слушают те, чьи вкусы пересекаются с вашими».</summary>
    public double Collaborative { get; set; }

    /// <summary>Аффинити к артистам и жанрам этого трека — накопленное, затухающее.</summary>
    public double Behavior { get; set; }

    /// <summary>Популярность по всей библиотеке. Единственный сигнал, работающий без истории.</summary>
    public double Popularity { get; set; }

    /// <summary>Как недавно трек появился в библиотеке.</summary>
    public double Freshness { get; set; }

    /// <summary>
    /// Насколько жанр трека недопредставлен в библиотеке. Противовес остальным термам: без него
    /// выдача сходится к самому населённому жанру, потому что там просто больше кандидатов.
    /// </summary>
    public double Coverage { get; set; }

    public double Total =>
        Taste + Content + Audio + Collaborative + Behavior + Popularity + Freshness + Coverage;

    // Без вектора нечего взвешивать: у холодного профиля Taste остаётся нулём.
    public static RankingWeights ColdDefaults() => new()
    {
        Popularity = 0.40,
        Freshness = 0.25,
        Coverage = 0.35,
    };

    public static RankingWeights WarmDefaults() => new()
    {
        Taste = 0.25,
        Content = 0.22,
        Audio = 0.10,
        Collaborative = 0.13,
        Behavior = 0.17,
        Popularity = 0.08,
        Freshness = 0.04,
        Coverage = 0.01,
    };

    public static RankingWeights MatureDefaults() => new()
    {
        Taste = 0.30,
        Content = 0.12,
        Audio = 0.10,
        Collaborative = 0.20,
        Behavior = 0.20,
        Popularity = 0.04,
        Freshness = 0.03,
        Coverage = 0.01,
    };

    // Поток: ведём от того, что играет сейчас. Audio здесь и есть «косинус к текущему треку».
    public static RankingWeights FlowDefaults() => new()
    {
        Taste = 0.30,
        Content = 0.20,
        Audio = 0.35,
        Collaborative = 0.10,
        Behavior = 0.04,
        Popularity = 0.01,
    };

    public static RankingWeights DiscoverDefaults() => new()
    {
        Taste = 0.12,
        Content = 0.18,
        Audio = 0.10,
        Collaborative = 0.15,
        Behavior = 0.20,
        Popularity = 0.03,
        Freshness = 0.10,
        Coverage = 0.12,
    };

    /// <summary>
    /// Взвешенная сумма с перенормировкой по присутствующим сигналам.
    /// <para>
    /// Подставлять вместо отсутствующего аудио контентный сигнал (<c>audio ?? content</c>)
    /// нельзя: «нет аудио» — не редкий сбой анализа, а обычное временное состояние, пока идёт
    /// многодневный бэкфилл. Подстановка молча поднимала бы такой трек до оценки
    /// заэмбежженного соседа.
    /// </para>
    /// <para>
    /// Вместо этого вес отсутствующего сигнала возвращается <b>всем</b> остальным термам
    /// пропорционально — та же идиома, что уже применяется в score.sql, где вес отсутствующего
    /// дескриптора перераспределяется, а не обнуляется.
    /// </para>
    /// </summary>
    public double Combine(
        double content,
        double? taste,
        double? audio,
        double collaborative,
        double behavior,
        double popularity,
        double freshness,
        double coverage)
    {
        var sum = Content * content
                  + Collaborative * collaborative
                  + Behavior * behavior
                  + Popularity * popularity
                  + Freshness * freshness
                  + Coverage * coverage;

        var present = Content + Collaborative + Behavior + Popularity + Freshness + Coverage;

        if (taste is { } tasteValue)
        {
            sum += Taste * tasteValue;
            present += Taste;
        }

        if (audio is { } audioValue)
        {
            sum += Audio * audioValue;
            present += Audio;
        }

        return present <= 0 ? 0 : sum / present * Total;
    }
}
