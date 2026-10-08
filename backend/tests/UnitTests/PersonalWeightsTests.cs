// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Xunit;
using Domain.Entities.Recommendations;
using App.Recommendations.Home;

namespace UnitTests.Recommendations;

public class PersonalWeightsTests
{
    private const int Taste = 0;
    private const int Audio = 2;

    private static readonly RankingWeights Hand = RankingWeights.Hand(ProfileMaturity.Mature);

    [Fact]
    public void A_listener_who_follows_the_sound_teaches_the_engine_to_trust_it()
    {
        // Дослушивает то, что звучит похоже (audio высокий), и бросает остальное, что бы ни говорил вкус.
        var random = new Random(7);
        var examples = Enumerable.Range(0, 400).Select(_ =>
        {
            var features = Features(random);
            return new RankingExample(features, features[Audio] > 0.5f);
        }).ToList();

        var learned = PersonalWeights.Fit(examples, Hand);

        Assert.True(learned[Audio] > Hand[Audio] * 2,
            $"audio weight {learned[Audio]:0.000}");
        Assert.Equal(1, learned.Values.Sum(), 6);
    }

    [Fact]
    public void Outcomes_unrelated_to_the_features_leave_the_weights_near_the_hand_ones()
    {
        var random = new Random(11);
        var examples = Enumerable.Range(0, 400)
            .Select(_ => new RankingExample(Features(random), random.NextDouble() < 0.5))
            .ToList();

        var learned = PersonalWeights.Fit(examples, Hand);

        for (var feature = 0; feature < RankingFeatures.Count; feature++)
            Assert.True(Math.Abs(learned[feature] - Hand[feature]) < 0.1, $"{RankingFeatures.Names[feature]}: {learned[feature]:0.000}");
    }

    [Fact]
    public void An_unknown_feature_does_not_count_for_or_against()
    {
        var examples = Enumerable.Range(0, 100)
            .Select(index => new RankingExample([.. Enumerable.Repeat(float.NaN, RankingFeatures.Count)], index % 2 == 0))
            .ToList();

        var learned = PersonalWeights.Fit(examples, Hand);

        for (var feature = 0; feature < RankingFeatures.Count; feature++)
            Assert.Equal(Hand[feature], learned[feature], 2);
    }

    [Fact]
    public void Learned_weights_take_over_gradually()
    {
        var learned = new RankingWeights([1, 0, 0, 0, 0, 0, 0, 0]);

        Assert.Same(Hand, new PersonalWeightsResult(null, 10, 0).Apply(Hand));

        var half = new PersonalWeightsResult(learned, PersonalWeights.HalfConfidence, 0.5).Apply(Hand);
        Assert.Equal((Hand[Taste] + 1) / 2, half[Taste], 6);
        Assert.Equal(1, half.Values.Sum(), 6);
    }

    [Theory]
    [InlineData("home:forYou", "forYou", true)]
    [InlineData("home:becauseYouListened", "becauseYouListened:abc", true)]
    [InlineData("home:dailyMix", "mixPool", true)]
    [InlineData("mix:daily", "mixPool", true)]
    [InlineData("home:forYou", "discover", false)]
    [InlineData("radio", "forYou", false)]
    [InlineData("album", "forYou", false)]
    public void A_start_is_traced_back_to_the_shelf_it_came_from(string source, string shelfKey, bool serves) =>
        Assert.Equal(serves, PersonalWeights.ShelfServes(source, shelfKey));

    [Fact]
    public void An_unknown_feature_is_stored_as_nan()
    {
        var encoded = PersonalWeights.Encode([0.5, null]);

        Assert.Equal(0.5f, encoded[0]);
        Assert.True(float.IsNaN(encoded[1]));
    }

    private static float[] Features(Random random) =>
        [.. Enumerable.Range(0, RankingFeatures.Count).Select(_ => (float)random.NextDouble())];
}
