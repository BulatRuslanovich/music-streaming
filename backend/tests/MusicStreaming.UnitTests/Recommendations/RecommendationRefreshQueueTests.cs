// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Recommendations;
using Xunit;

namespace MusicStreaming.UnitTests.Recommendations;

public class RecommendationRefreshQueueTests
{
    [Fact]
    public void Strong_feedback_upgrades_an_already_pending_refresh()
    {
        var queue = new RecommendationRefreshQueue();
        var userId = Guid.CreateVersion7();
        var now = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

        queue.MarkDirty(userId, now);
        queue.MarkDirty(userId, now.AddSeconds(10), forceRebuild: true);

        var claimed = queue.ClaimSettled(now.AddSeconds(61), TimeSpan.FromSeconds(60));

        var refresh = Assert.Single(claimed);
        Assert.Equal(userId, refresh.UserId);
        Assert.True(refresh.ForceRebuild);
    }

    [Fact]
    public void A_pending_refresh_is_claimed_only_once()
    {
        var queue = new RecommendationRefreshQueue();
        var userId = Guid.CreateVersion7();
        var now = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

        queue.MarkDirty(userId, now);

        Assert.Single(queue.ClaimSettled(now.AddMinutes(2), TimeSpan.FromMinutes(1)));
        Assert.Empty(queue.ClaimSettled(now.AddMinutes(3), TimeSpan.FromMinutes(1)));
    }
}
