// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Common;
using Infrastructure.Imaging;
using Xunit;

namespace UnitTests.Imaging;

public class CoverRenditionPlanTests
{
    private static IReadOnlyList<(int Edge, int Target)> Plan(int sourceEdge) =>
        ImageSharpImageProcessor.PlanRenditions(CoverVariants.Edges, sourceEdge);

    [Fact]
    public void A_large_source_gets_every_variant_at_its_own_size() =>
        Assert.Equal([(1024, 1024), (640, 640), (256, 256)], Plan(3000));

    [Fact]
    public void A_mid_sized_source_keeps_its_resolution_as_the_full_cover() =>
        Assert.Equal([(640, 600), (256, 256)], Plan(600));

    [Fact]
    public void A_source_between_full_and_large_becomes_the_large_cover() =>
        Assert.Equal([(1024, 800), (640, 640), (256, 256)], Plan(800));

    [Fact]
    public void A_tiny_source_is_stored_once_and_never_upscaled() =>
        Assert.Equal([(256, 200)], Plan(200));
}
