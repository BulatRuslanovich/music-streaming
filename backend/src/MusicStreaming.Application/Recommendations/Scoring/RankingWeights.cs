// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations.Scoring;

public class RankingWeights
{
    /// <summary>
    /// Насколько трек похож на вектор вкуса слушателя. Того, чего у скалярных аффинити нет:
    /// они знают, каких артистов человек любит, но не то, как он звучит.
    /// </summary>
    public double Taste { get; set; }

    public double Content { get; set; }

    /// <summary>Косинус к сидам — «похоже на то, что вы только что слушали».</summary>
    public double Audio { get; set; }

    public double Collaborative { get; set; }
    public double Behavior { get; set; }
    public double Popularity { get; set; }
    public double Freshness { get; set; }
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
    /// Раньше отсутствующий аудио-сигнал заменялся контентным (<c>audio ?? content</c>). С
    /// эмбеддингами «нет аудио» перестало быть редким сбоем анализа и стало обычным временным
    /// состоянием: пока идёт многодневный бэкфилл, у половины библиотеки вектора нет. Подстановка
    /// молча поднимала бы такой трек до оценки заэмбежженного соседа.
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
