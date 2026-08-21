// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations.Scoring;
using MusicStreaming.Application.Services.Recommendations;
using MusicStreaming.Domain.Entities.Recommendations;
using Xunit;

using static MusicStreaming.UnitTests.Recommendations.CandidateBuilder;

namespace MusicStreaming.UnitTests.Recommendations;

public class DjSelectionPolicyTests
{
    [Theory]
    [InlineData(DjVariety.Familiar, 0.10)]
    [InlineData(DjVariety.Balanced, 0.35)]
    [InlineData(DjVariety.Adventurous, 0.70)]
    public void Variety_maps_to_a_stable_exploration_ratio(DjVariety variety, double expected) =>
        Assert.Equal(expected, DjSelectionPolicy.ExplorationRatio(variety));

    [Fact]
    public void Flow_prioritises_seed_similarity_over_global_popularity()
    {
        var similar = Candidate();
        similar.Content = 0.9;

        var popular = Candidate();
        popular.Content = 0.1;
        popular.Popularity = 1;

        Score(similar, DjMode.Flow);
        Score(popular, DjMode.Flow);

        Assert.True(similar.Score > popular.Score);
    }

    [Fact]
    public void Discover_prioritises_an_unheard_track_adjacent_to_known_taste()
    {
        var artist = Guid.CreateVersion7();
        var adjacent = Candidate(artistId: artist, novel: true);
        var generic = Candidate(novel: true);
        generic.Popularity = 1;

        var context = new RankingContext(
            new Dictionary<Guid, double> { [artist] = 0.8 },
            new Dictionary<Guid, double>(),
            new Dictionary<Guid, TrackHistory>(),
            new Dictionary<Guid, DateTimeOffset>(),
            Now);

        Score(adjacent, DjMode.Discover, context);
        Score(generic, DjMode.Discover, context);

        Assert.True(adjacent.Score > generic.Score);
    }

    [Fact]
    public void Rediscover_prioritises_the_stronger_historical_relationship()
    {
        var strong = Candidate();
        var weak = Candidate();
        var context = new RankingContext(
            new Dictionary<Guid, double>(),
            new Dictionary<Guid, double>(),
            new Dictionary<Guid, TrackHistory>
            {
                [strong.TrackId] = new(Now.AddDays(-90), 8, 0, 0.95, 0.7, CompletedCount: 7, ReplayCount: 2),
                [weak.TrackId] = new(Now.AddDays(-90), 1, 0, 0.55, 0.1),
            },
            new Dictionary<Guid, DateTimeOffset>(),
            Now);

        Score(strong, DjMode.Rediscover, context);
        Score(weak, DjMode.Rediscover, context);

        Assert.True(strong.Score > weak.Score);
    }

    [Fact]
    public void Independent_evidence_gives_a_bounded_ranking_bonus()
    {
        var confirmed = Candidate();
        confirmed.Content = 0.6;
        confirmed.EvidenceCount = 4;

        var singleSource = Candidate();
        singleSource.Content = 0.6;

        Score(confirmed, DjMode.ForYou);
        Score(singleSource, DjMode.ForYou);

        Assert.True(confirmed.Score > singleSource.Score);
        Assert.True(confirmed.Score < singleSource.Score * 1.5);
    }

    private static void Score(
        MusicStreaming.Application.Recommendations.RecommendationCandidate candidate,
        DjMode mode,
        RankingContext? context = null) =>
        DjSelectionPolicy.Score(
            candidate,
            context ?? RankingContext.Empty(Now),
            RankingWeights.MatureDefaults(),
            new RecommendationOptions(),
            mode);
}
