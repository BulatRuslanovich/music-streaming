// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations.Scoring;
using MusicStreaming.Domain.Entities.Recommendations;
using Xunit;

using static MusicStreaming.UnitTests.Recommendations.CandidateBuilder;

namespace MusicStreaming.UnitTests.Recommendations;

public class CandidateScorerTests
{
    private static RankingContext Context(
        Dictionary<Guid, double>? artists = null,
        Dictionary<Guid, double>? genres = null,
        Dictionary<Guid, TrackHistory>? history = null,
        Dictionary<Guid, DateTimeOffset>? shown = null) =>
        new(artists ?? [], genres ?? [], history ?? [], shown ?? [], Now);

    [Fact]
    public void An_unknown_artist_and_genre_score_neutral() =>
        Assert.Equal(0, CandidateScorer.BehaviorScore(Candidate(), Context()));

    [Fact]
    public void Artist_affinity_outweighs_genre_affinity()
    {
        var artist = Guid.CreateVersion7();
        var genre = Guid.CreateVersion7();
        var candidate = Candidate(artistId: artist, genreId: genre);

        var lovedArtist = CandidateScorer.BehaviorScore(
            candidate, Context(artists: new() { [artist] = 1.0 }));

        var lovedGenre = CandidateScorer.BehaviorScore(
            candidate, Context(genres: new() { [genre] = 1.0 }));

        Assert.True(lovedArtist > lovedGenre);
    }

    [Fact]
    public void A_disliked_artist_scores_below_an_unknown_one()
    {
        var artist = Guid.CreateVersion7();
        var candidate = Candidate(artistId: artist);

        var disliked = CandidateScorer.BehaviorScore(candidate, Context(artists: new() { [artist] = -0.8 }));

        Assert.True(disliked < 0);
        Assert.True(disliked >= -1);
    }

    [Fact]
    public void Behaviour_stays_inside_the_unit_interval()
    {
        var artist = Guid.CreateVersion7();
        var genre = Guid.CreateVersion7();

        var score = CandidateScorer.BehaviorScore(
            Candidate(artistId: artist, genreId: genre),
            Context(artists: new() { [artist] = 1.0 }, genres: new() { [genre] = 1.0 }));

        Assert.InRange(score, -1, 1);
    }

    [Fact]
    public void An_untouched_candidate_is_not_penalised() =>
        Assert.Equal(1.0, CandidateScorer.PenaltyFor(Candidate(), Context(), new RecommendationOptions()));

    [Fact]
    public void Something_just_played_is_pushed_far_down()
    {
        var options = new RecommendationOptions();
        var candidate = Candidate();

        var history = new Dictionary<Guid, TrackHistory>
        {
            [candidate.TrackId] = new(Now.AddHours(-1), PlayCount: 1, SkipCount: 0, AverageCompletion: 1, Score: 0.5),
        };

        var penalty = CandidateScorer.PenaltyFor(candidate, Context(history: history), options);

        Assert.Equal(options.JustPlayedPenalty, penalty);
    }

    [Fact]
    public void Penalties_taper_as_a_play_recedes()
    {
        var options = new RecommendationOptions();
        var candidate = Candidate();

        double PenaltyAfter(TimeSpan ago) => CandidateScorer.PenaltyFor(
            candidate,
            Context(history: new()
            {
                [candidate.TrackId] = new(Now - ago, 1, 0, 1, 0.5),
            }),
            options);

        var justNow = PenaltyAfter(TimeSpan.FromHours(1));
        var lastWeek = PenaltyAfter(TimeSpan.FromDays(3));
        var lastMonth = PenaltyAfter(TimeSpan.FromDays(30));

        Assert.True(justNow < lastWeek);
        Assert.True(lastWeek < lastMonth);
        Assert.Equal(1.0, lastMonth);
    }

    [Fact]
    public void A_repeatedly_abandoned_track_is_suppressed()
    {
        var options = new RecommendationOptions();
        var candidate = Candidate();

        var penalty = CandidateScorer.PenaltyFor(
            candidate,
            Context(history: new()
            {
                [candidate.TrackId] = new(Now.AddDays(-30), PlayCount: 3, SkipCount: 3, AverageCompletion: 0.05, Score: -0.5),
            }),
            options);

        Assert.Equal(options.DislikedTrackPenalty, penalty);
    }

    [Fact]
    public void A_track_shown_and_ignored_is_held_back()
    {
        var options = new RecommendationOptions();
        var candidate = Candidate();

        var penalty = CandidateScorer.PenaltyFor(
            candidate,
            Context(shown: new() { [candidate.TrackId] = Now.AddDays(-1) }),
            options);

        Assert.Equal(options.UnclickedImpressionPenalty, penalty);
    }

    [Fact]
    public void An_old_impression_stops_counting()
    {
        var options = new RecommendationOptions();
        var candidate = Candidate();

        var penalty = CandidateScorer.PenaltyFor(
            candidate,
            Context(shown: new() { [candidate.TrackId] = Now.AddDays(-options.ImpressionCooldownDays - 1) }),
            options);

        Assert.Equal(1.0, penalty);
    }

    [Fact]
    public void Scoring_combines_merit_with_the_penalty()
    {
        var options = new RecommendationOptions();
        var weights = RankingWeights.MatureDefaults();

        var candidate = Candidate(score: 0);
        candidate.Content = 1;
        candidate.Collaborative = 1;
        candidate.Popularity = 1;

        CandidateScorer.Score(candidate, Context(), weights, options);
        var clean = candidate.Score;

        candidate.Content = 1;
        candidate.Collaborative = 1;
        candidate.Popularity = 1;

        CandidateScorer.Score(
            candidate,
            Context(history: new() { [candidate.TrackId] = new(Now.AddHours(-1), 1, 0, 1, 0.5) }),
            weights,
            options);

        Assert.True(clean > 0);
        Assert.Equal(clean * options.JustPlayedPenalty, candidate.Score, precision: 10);
    }

    [Theory]
    [InlineData(ProfileMaturity.Cold)]
    [InlineData(ProfileMaturity.Warm)]
    [InlineData(ProfileMaturity.Mature)]
    public void Every_weight_set_sums_to_one(ProfileMaturity maturity)
    {
        var weights = new RecommendationOptions().WeightsFor(maturity);

        Assert.Equal(1.0, weights.Total, precision: 10);
    }

    [Fact]
    public void Cold_ranking_ignores_personal_signals()
    {
        var cold = RankingWeights.ColdDefaults();

        Assert.Equal(0, cold.Content);
        Assert.Equal(0, cold.Collaborative);
        Assert.Equal(0, cold.Behavior);
        Assert.True(cold.Popularity > 0);
        Assert.True(cold.Coverage > 0);
    }

    [Fact]
    public void Collaborative_signal_gains_weight_with_maturity() =>
        Assert.True(RankingWeights.MatureDefaults().Collaborative > RankingWeights.WarmDefaults().Collaborative);

    [Fact]
    public void Popularity_loses_weight_with_maturity() =>
        Assert.True(RankingWeights.MatureDefaults().Popularity < RankingWeights.ColdDefaults().Popularity);

    [Fact]
    public void A_disliked_guest_does_not_sink_a_loved_headliner()
    {
        var headliner = Guid.CreateVersion7();
        var guest = Guid.CreateVersion7();

        var candidate = Candidate(artistId: headliner, artistIds: [headliner, guest]);

        var context = Context(artists: new() { [headliner] = 0.9, [guest] = -0.9 });

        Assert.True(CandidateScorer.BehaviorScore(candidate, context) > 0);
    }

    [Fact]
    public void A_disliked_headliner_still_scores_negative()
    {
        var headliner = Guid.CreateVersion7();
        var candidate = Candidate(artistId: headliner);

        Assert.True(
            CandidateScorer.BehaviorScore(candidate, Context(artists: new() { [headliner] = -0.9 })) < 0);
    }

    [Fact]
    public void A_track_the_library_always_abandons_is_held_back()
    {
        var options = new RecommendationOptions();

        var abandoned = Candidate();
        abandoned.GlobalSkipRate = 1.0;

        var kept = Candidate();
        kept.GlobalSkipRate = 0.1;

        Assert.Equal(options.HighSkipRatePenalty, CandidateScorer.QualityFactor(abandoned, options));
        Assert.Equal(1.0, CandidateScorer.QualityFactor(kept, options));
    }

    [Fact]
    public void Without_enough_plays_the_global_skip_rate_is_ignored() =>
        Assert.Equal(1.0, CandidateScorer.QualityFactor(Candidate(), new RecommendationOptions()));

    [Fact]
    public void A_track_from_the_listeners_era_outranks_a_distant_one()
    {
        var options = new RecommendationOptions();
        var context = Context() with { YearCenter = 1995, YearSpread = 5 };

        var inEra = CandidateScorer.EraFactor(Candidate(year: 1995), context, options);
        var offEra = CandidateScorer.EraFactor(Candidate(year: 2025), context, options);

        Assert.Equal(1.0, inEra, precision: 10);
        Assert.InRange(offEra, options.EraFitFloor, inEra);
    }

    [Fact]
    public void Without_a_year_taste_nothing_is_nudged() =>
        Assert.Equal(
            1.0, CandidateScorer.EraFactor(Candidate(year: 1970), Context(), new RecommendationOptions()));

    [Fact]
    public void A_candidate_without_audio_features_is_scored_on_its_content_alone()
    {
        var weights = RankingWeights.MatureDefaults();

        Assert.Equal(
            weights.Combine(0.8, null, 0, 0, 0, 0, 0),
            weights.Combine(0.8, 0.8, 0, 0, 0, 0, 0),
            precision: 10);
    }

    [Fact]
    public void Audio_similarity_carries_weight_for_a_mature_profile() =>
        Assert.True(RankingWeights.MatureDefaults().Audio > 0);

    [Fact]
    public void Coverage_keeps_a_say_once_the_profile_warms_up()
    {
        Assert.True(RankingWeights.WarmDefaults().Coverage > 0);
        Assert.True(RankingWeights.MatureDefaults().Coverage > 0);
    }

    [Fact]
    public void Dj_intent_weight_sets_are_normalised()
    {
        Assert.Equal(1.0, RankingWeights.FlowDefaults().Total, precision: 10);
        Assert.Equal(1.0, RankingWeights.DiscoverDefaults().Total, precision: 10);
    }
}
