// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Domain.Entities.Recommendations;
using Xunit;

using static UnitTests.Recommendations.CandidateBuilder;
using App.Recommendations;
using App.Recommendations.Home;

namespace UnitTests.Recommendations;

public class CandidateScorerTests
{
    private static RankingContext Context(
        Dictionary<Guid, double>? artists = null,
        Dictionary<Guid, double>? genres = null,
        Dictionary<Guid, TrackHistory>? history = null) =>
        new(artists ?? [], genres ?? [], history ?? [], Now);

    private static double Behavior(RecommendationCandidate candidate, RankingContext context)
    {
        CandidateScorer.Score(candidate, context, ProfileMaturity.Mature);
        return candidate.Behavior;
    }

    private static double Scored(RecommendationCandidate candidate, RankingContext context)
    {
        candidate.Content = 1;
        candidate.Collaborative = 1;
        candidate.Popularity = 1;

        CandidateScorer.Score(candidate, context, ProfileMaturity.Mature);
        return candidate.Score;
    }

    // Штраф наблюдается снаружи как отношение счёта к счёту того же кандидата без истории.
    private static double Penalty(RecommendationCandidate candidate, RankingContext context) =>
        Scored(candidate, context) / Scored(candidate, Context());

    private static double FactorOf(RecommendationCandidate candidate, RecommendationCandidate neutral, RankingContext context) =>
        Scored(candidate, context) / Scored(neutral, Context());

    [Fact]
    public void An_unknown_artist_and_genre_score_neutral() =>
        Assert.Equal(0, Behavior(Candidate(), Context()));

    [Fact]
    public void Artist_affinity_outweighs_genre_affinity()
    {
        var artist = Guid.CreateVersion7();
        var genre = Guid.CreateVersion7();
        var candidate = Candidate(artistId: artist, genreId: genre);

        var lovedArtist = Behavior(
            candidate, Context(artists: new() { [artist] = 1.0 }));

        var lovedGenre = Behavior(
            candidate, Context(genres: new() { [genre] = 1.0 }));

        Assert.True(lovedArtist > lovedGenre);
    }

    [Fact]
    public void A_disliked_artist_scores_below_an_unknown_one()
    {
        var artist = Guid.CreateVersion7();
        var candidate = Candidate(artistId: artist);

        var disliked = Behavior(candidate, Context(artists: new() { [artist] = -0.8 }));

        Assert.True(disliked < 0);
        Assert.True(disliked >= -1);
    }

    [Fact]
    public void Behaviour_stays_inside_the_unit_interval()
    {
        var artist = Guid.CreateVersion7();
        var genre = Guid.CreateVersion7();

        var score = Behavior(
            Candidate(artistId: artist, genreId: genre),
            Context(artists: new() { [artist] = 1.0 }, genres: new() { [genre] = 1.0 }));

        Assert.InRange(score, -1, 1);
    }

    [Fact]
    public void An_untouched_candidate_is_not_penalised() =>
        Assert.Equal(1.0, Penalty(Candidate(), Context()), precision: 10);

    [Fact]
    public void Something_just_played_is_pushed_far_down()
    {
        var candidate = Candidate();

        var history = new Dictionary<Guid, TrackHistory>
        {
            [candidate.TrackId] = new(Now.AddHours(-1), PlayCount: 1, SkipCount: 0, AverageCompletion: 1, Score: 0.5),
        };

        var penalty = Penalty(candidate, Context(history: history));

        Assert.Equal(RecommendationTuning.Penalties.JustPlayed, penalty, precision: 10);
    }

    [Fact]
    public void Penalties_taper_as_a_play_recedes()
    {
        var candidate = Candidate();

        double PenaltyAfter(TimeSpan ago) => Penalty(
            candidate,
            Context(history: new()
            {
                [candidate.TrackId] = new(Now - ago, 1, 0, 1, 0.5),
            }));

        var justNow = PenaltyAfter(TimeSpan.FromHours(1));
        var lastWeek = PenaltyAfter(TimeSpan.FromDays(3));
        var lastMonth = PenaltyAfter(TimeSpan.FromDays(30));

        Assert.True(justNow < lastWeek);
        Assert.True(lastWeek < lastMonth);
        Assert.Equal(1.0, lastMonth, precision: 10);
    }

    [Fact]
    public void A_repeatedly_abandoned_track_is_suppressed()
    {
        var candidate = Candidate();

        var penalty = Penalty(
            candidate,
            Context(history: new()
            {
                [candidate.TrackId] = new(Now.AddDays(-30), PlayCount: 3, SkipCount: 3, AverageCompletion: 0.05, Score: -0.5),
            }));

        Assert.Equal(RecommendationTuning.Penalties.DislikedTrack, penalty, precision: 10);
    }

    [Fact]
    public void Scoring_combines_merit_with_the_penalty()
    {
        var candidate = Candidate(score: 0);
        var clean = Scored(candidate, Context());

        var penalised = Scored(
            candidate,
            Context(history: new() { [candidate.TrackId] = new(Now.AddHours(-1), 1, 0, 1, 0.5) }));

        Assert.True(clean > 0);
        Assert.Equal(clean * RecommendationTuning.Penalties.JustPlayed, penalised, precision: 10);
    }

    [Fact]
    public void Cold_ranking_ignores_personal_signals()
    {
        var plain = Candidate();
        plain.Popularity = 0.5;
        plain.Freshness = 0.5;
        plain.Coverage = 0.5;
        CandidateScorer.Score(plain, Context(), ProfileMaturity.Cold);

        var personal = plain.WithScore(0);
        personal.Content = 1;
        personal.Collaborative = 1;
        personal.TasteFit = 1;
        personal.AudioSimilarity = 1;
        CandidateScorer.Score(personal, Context(), ProfileMaturity.Cold);

        Assert.True(plain.Score > 0);
        Assert.Equal(plain.Score, personal.Score, precision: 10);
    }

    [Fact]
    public void A_disliked_guest_does_not_sink_a_loved_headliner()
    {
        var headliner = Guid.CreateVersion7();
        var guest = Guid.CreateVersion7();

        var candidate = Candidate(artistId: headliner, artistIds: [headliner, guest]);

        var context = Context(artists: new() { [headliner] = 0.9, [guest] = -0.9 });

        Assert.True(Behavior(candidate, context) > 0);
    }

    [Fact]
    public void A_disliked_headliner_still_scores_negative()
    {
        var headliner = Guid.CreateVersion7();
        var candidate = Candidate(artistId: headliner);

        Assert.True(
            Behavior(candidate, Context(artists: new() { [headliner] = -0.9 })) < 0);
    }

    [Fact]
    public void A_track_the_library_always_abandons_is_held_back()
    {
        var abandoned = Candidate();
        abandoned.GlobalSkipRate = 1.0;

        var kept = Candidate();
        kept.GlobalSkipRate = 0.1;

        Assert.Equal(RecommendationTuning.Penalties.HighSkipRatePenalty,
            FactorOf(abandoned, Twin(abandoned), Context()), precision: 10);
        Assert.Equal(1.0, FactorOf(kept, Twin(kept), Context()), precision: 10);
    }

    [Fact]
    public void Without_enough_plays_the_global_skip_rate_is_ignored()
    {
        var candidate = Candidate();
        Assert.Equal(1.0, FactorOf(candidate, Twin(candidate), Context()), precision: 10);
    }

    private static RecommendationCandidate Twin(RecommendationCandidate candidate)
    {
        var twin = candidate.WithScore(0);
        twin.GlobalSkipRate = null;
        return twin;
    }

    [Fact]
    public void A_track_from_the_listeners_era_outranks_a_distant_one()
    {
        var context = Context() with { YearCenter = 1995, YearSpread = 5 };

        var inEra = Penalty(Candidate(year: 1995), context);
        var offEra = Penalty(Candidate(year: 2025), context);

        Assert.Equal(1.0, inEra, precision: 10);
        Assert.InRange(offEra, RecommendationTuning.Penalties.EraFitFloor, inEra);
    }

    [Fact]
    public void Without_a_year_taste_nothing_is_nudged() =>
        Assert.Equal(1.0, Penalty(Candidate(year: 1970), Context()), precision: 10);

    // Все признаки равны 0.8; поведенческий берётся из контекста: 0.7 * артист + 0.3 * жанр = 0.8.
    private static double Uniform(double? sonic)
    {
        var artist = Guid.CreateVersion7();
        var genre = Guid.CreateVersion7();

        var candidate = Candidate(artistId: artist, genreId: genre);
        candidate.Content = 0.8;
        candidate.Collaborative = 0.8;
        candidate.Popularity = 0.8;
        candidate.Freshness = 0.8;
        candidate.Coverage = 0.8;
        candidate.TasteFit = sonic;
        candidate.AudioSimilarity = sonic;

        CandidateScorer.Score(
            candidate,
            Context(artists: new() { [artist] = 0.8 }, genres: new() { [genre] = 0.8 }),
            ProfileMaturity.Mature);

        return candidate.Score;
    }

    [Fact]
    public void A_candidate_without_an_embedding_is_judged_only_on_what_is_known_about_it() =>
        Assert.Equal(Uniform(0.8), Uniform(null), precision: 10);
}
