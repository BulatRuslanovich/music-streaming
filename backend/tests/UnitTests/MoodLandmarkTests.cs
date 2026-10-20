// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Services;
using Xunit;

namespace UnitTests;

public class MoodLandmarkTests
{
    [Fact]
    public void The_label_sits_on_the_densest_cluster_not_between_clusters()
    {
        // Плотная кучка слева и две редкие точки справа: медиана по осям ушла бы в пустоту между ними.
        List<(float X, float Y, float Weight)> spots =
        [
            (0.10f, 0.20f, 1f), (0.12f, 0.22f, 1f), (0.11f, 0.18f, 1f), (0.13f, 0.21f, 1f),
            (0.85f, 0.80f, 1f), (0.90f, 0.85f, 1f), (0.88f, 0.15f, 1f),
        ];

        var (x, y) = LibraryMapService.DensestSpot(spots);

        Assert.InRange(x, 0.09f, 0.14f);
        Assert.InRange(y, 0.17f, 0.23f);
    }

    [Fact]
    public void Better_matching_tracks_outweigh_a_crowd_of_weak_ones()
    {
        List<(float X, float Y, float Weight)> spots =
        [
            (0.20f, 0.50f, 0.1f), (0.21f, 0.51f, 0.1f), (0.19f, 0.49f, 0.1f),
            (0.80f, 0.50f, 1f), (0.81f, 0.51f, 1f),
        ];

        var (x, _) = LibraryMapService.DensestSpot(spots);

        Assert.InRange(x, 0.78f, 0.83f);
    }

    [Fact]
    public void A_single_track_is_its_own_landmark() =>
        Assert.Equal((0.4f, 0.6f), LibraryMapService.DensestSpot([(0.4f, 0.6f, 0.7f)]));
}
