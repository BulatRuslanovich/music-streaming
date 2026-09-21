// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Recommendations;
using MusicStreaming.Application.Recommendations.Scoring;
using Xunit;

using static MusicStreaming.UnitTests.Recommendations.CandidateBuilder;

namespace MusicStreaming.UnitTests.Recommendations;

/// <summary>
/// Exploration по звучанию, а не по новизне в каталоге. <c>IsNovel</c> отвечает на вопрос
/// «слышал ли пользователь этот трек», и по нему в exploration попадал очередной трек любимого
/// жанра просто потому, что до него не дошли руки. Здесь проверяется, что far-корзина
/// действительно собирает то, что звучит иначе.
/// </summary>
public class SonicExplorationTests
{
    /// <summary>Пул, где близость к вкусу равномерно размазана по 0..1.</summary>
    private static List<RecommendationCandidate> GradedPool(int count) =>
    [
        .. Enumerable.Range(0, count).Select(index =>
            Candidate(
                score: 0.5,
                tasteFit: index / (double)(count - 1),
                embeddingRow: index)),
    ];

    [Fact]
    public void Exploration_picks_what_sounds_different_not_merely_what_is_unheard()
    {
        var shelf = Explorer.Compose(GradedPool(100), 12, 0.25, Options(), seed: 1);

        var fits = shelf.Select(candidate => candidate.TasteFit!.Value).ToList();

        // Треть полки должна лежать в нижнем квартиле по близости к вкусу.
        Assert.Equal(12, shelf.Count);
        Assert.True(fits.Count(fit => fit <= 0.25) >= 3);
    }

    [Fact]
    public void The_explore_slots_really_are_the_far_ones()
    {
        var pool = GradedPool(100);
        var shelf = Explorer.Compose(pool, 12, 0.25, Options(), seed: 5);

        var fits = shelf.Select(candidate => candidate.TasteFit!.Value).OrderBy(fit => fit).ToList();
        var median = fits[fits.Count / 2];

        // Самые далёкие элементы полки заметно ниже её медианы — значит корзина не случайна.
        Assert.True(fits[0] < 0.25);
        Assert.True(fits[0] < median);
    }

    [Fact]
    public void A_track_without_an_embedding_is_never_called_far_from_your_taste()
    {
        // Его близость к вкусу неизвестна; назвать его «далёким» было бы неправдой.
        var known = GradedPool(60);
        var unknown = Enumerable.Range(0, 20).Select(_ => Candidate(score: 0.9)).ToList();

        var shelf = Explorer.Compose([.. known, .. unknown], 12, 0.25, Options(), seed: 2);
        var unknownIds = unknown.Select(candidate => candidate.TrackId).ToHashSet();

        // Треки без вектора могут попасть в полку, но только как exploit: у них высокий Score.
        var farmost = shelf.Where(c => c.TasteFit is { } fit && fit <= 0.25).ToList();

        Assert.NotEmpty(farmost);
        Assert.DoesNotContain(farmost, candidate => unknownIds.Contains(candidate.TrackId));
    }

    [Fact]
    public void Exploration_never_opens_the_shelf()
    {
        // По первому треку слушатель судит о всей полке.
        for (var seed = 0; seed < 50; seed++)
        {
            var shelf = Explorer.Compose(GradedPool(100), 12, 0.25, Options(), seed);

            Assert.True(shelf[0].TasteFit > 0.25, $"seed {seed} opened the shelf with an explore pick");
        }
    }

    [Fact]
    public void Without_exploration_the_shelf_stays_close_to_the_taste()
    {
        var shelf = Explorer.Compose(GradedPool(100), 12, 0, Options(), seed: 1);

        Assert.DoesNotContain(shelf, candidate => candidate.TasteFit <= 0.25);
    }

    [Fact]
    public void A_pool_with_no_embeddings_falls_back_to_catalogue_novelty()
    {
        // Пока эмбеддингов нет вовсе, лучше прежнее поведение, чем никакого.
        List<RecommendationCandidate> pool =
        [
            .. Enumerable.Range(0, 50).Select(index => Candidate(score: 0.9 - index * 0.001)),
            .. Enumerable.Range(0, 50).Select(index => Candidate(score: 0.5 - index * 0.001, novel: true)),
        ];

        var shelf = Explorer.Compose(pool, 12, 0.25, Options(), seed: 1);

        Assert.Equal(12, shelf.Count);
        Assert.Equal(3, shelf.Count(candidate => candidate.IsNovel));
    }

    [Fact]
    public void The_same_seed_gives_the_same_far_basket()
    {
        var pool = GradedPool(100);

        var first = Explorer.Compose(pool, 12, 0.25, Options(), seed: 99);
        var second = Explorer.Compose(pool, 12, 0.25, Options(), seed: 99);

        Assert.Equal(first.Select(c => c.TrackId), second.Select(c => c.TrackId));
    }

    [Fact]
    public void A_different_day_rotates_the_far_basket()
    {
        var pool = GradedPool(100);

        var today = Explorer.Compose(pool, 12, 0.25, Options(), seed: 1);
        var tomorrow = Explorer.Compose(pool, 12, 0.25, Options(), seed: 2);

        Assert.NotEqual(today.Select(c => c.TrackId), tomorrow.Select(c => c.TrackId));
    }

    [Fact]
    public void A_higher_ratio_reaches_further_from_the_taste()
    {
        var pool = GradedPool(100);

        var timid = Explorer.Compose(pool, 12, 0.10, Options(), seed: 4);
        var bold = Explorer.Compose(pool, 12, 0.60, Options(), seed: 4);

        Assert.True(
            bold.Average(c => c.TasteFit!.Value) < timid.Average(c => c.TasteFit!.Value));
    }
}
