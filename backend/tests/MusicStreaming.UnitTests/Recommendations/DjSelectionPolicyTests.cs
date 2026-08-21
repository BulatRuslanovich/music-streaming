// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services.Recommendations;
using Xunit;

namespace MusicStreaming.UnitTests.Recommendations;

public class DjSelectionPolicyTests
{
    [Theory]
    [InlineData(DjVariety.Familiar, 0.10)]
    [InlineData(DjVariety.Balanced, 0.35)]
    [InlineData(DjVariety.Adventurous, 0.70)]
    public void Variety_maps_to_a_stable_exploration_ratio(DjVariety variety, double expected) =>
        Assert.Equal(expected, DjSelectionPolicy.ExplorationRatio(variety));
}
