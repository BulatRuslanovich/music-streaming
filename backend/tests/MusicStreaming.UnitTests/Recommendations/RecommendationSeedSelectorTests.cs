// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Recommendations.Scoring;
using MusicStreaming.Application.Services.Recommendations;
using Xunit;

using static MusicStreaming.UnitTests.Recommendations.CandidateBuilder;

namespace MusicStreaming.UnitTests.Recommendations;

public class RecommendationSeedSelectorTests
{
    [Fact]
    public void Repeated_early_skips_are_not_used_as_positive_seeds()
    {
        var abandoned = Guid.CreateVersion7();
        var loved = Guid.CreateVersion7();
        var history = new Dictionary<Guid, TrackHistory>
        {
            [abandoned] = new(Now.AddHours(-1), 3, 3, 0.08, 0.05),
            [loved] = new(Now.AddDays(-5), 3, 0, 0.92, 0.45, CompletedCount: 3),
        };

        var seeds = RecommendationSeedSelector.Select(history, Now, 20);

        Assert.DoesNotContain(seeds, seed => seed.TrackId == abandoned);
        Assert.Contains(seeds, seed => seed.TrackId == loved);
    }

    [Fact]
    public void Engagement_and_affinity_outweigh_bare_recency()
    {
        var recentWeak = Guid.CreateVersion7();
        var established = Guid.CreateVersion7();
        var history = new Dictionary<Guid, TrackHistory>
        {
            [recentWeak] = new(Now.AddHours(-1), 1, 0, 0.51, 0.08),
            [established] = new(Now.AddDays(-7), 6, 0, 0.96, 0.55, CompletedCount: 5, ReplayCount: 1),
        };

        var seeds = RecommendationSeedSelector.Select(history, Now, 20);

        Assert.Equal(established, seeds[0].TrackId);
        Assert.True(seeds[0].Weight > seeds[1].Weight);
    }
}
