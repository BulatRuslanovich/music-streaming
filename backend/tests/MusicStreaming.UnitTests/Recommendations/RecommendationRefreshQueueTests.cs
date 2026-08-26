// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Recommendations;
using Xunit;

namespace MusicStreaming.UnitTests.Recommendations;

public class RecommendationRefreshQueueTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(300);

    [Fact]
    public void Strong_feedback_upgrades_an_already_pending_refresh()
    {
        var queue = new RecommendationRefreshQueue();
        var userId = Guid.CreateVersion7();

        queue.MarkDirty(userId, Now);
        queue.MarkDirty(userId, Now.AddSeconds(10), forceRebuild: true);

        var claimed = queue.ClaimSettled(Now.AddSeconds(71), Debounce, MaxDelay);

        var refresh = Assert.Single(claimed);
        Assert.Equal(userId, refresh.UserId);
        Assert.True(refresh.ForceRebuild);
    }

    [Fact]
    public void A_pending_refresh_is_claimed_only_once()
    {
        var queue = new RecommendationRefreshQueue();
        var userId = Guid.CreateVersion7();

        queue.MarkDirty(userId, Now);

        Assert.Single(queue.ClaimSettled(Now.AddMinutes(2), Debounce, MaxDelay));
        Assert.Empty(queue.ClaimSettled(Now.AddMinutes(3), Debounce, MaxDelay));
    }

    [Fact]
    public void Continued_activity_postpones_the_refresh()
    {
        var queue = new RecommendationRefreshQueue();
        var userId = Guid.CreateVersion7();

        queue.MarkDirty(userId, Now);
        queue.MarkDirty(userId, Now.AddSeconds(50));

        Assert.Empty(queue.ClaimSettled(Now.AddSeconds(70), Debounce, MaxDelay));
        Assert.Single(queue.ClaimSettled(Now.AddSeconds(111), Debounce, MaxDelay));
    }

    [Fact]
    public void An_endlessly_active_user_is_refreshed_once_the_delay_ceiling_is_reached()
    {
        var queue = new RecommendationRefreshQueue();
        var userId = Guid.CreateVersion7();

        for (var second = 0; second <= 300; second += 30)
            queue.MarkDirty(userId, Now.AddSeconds(second));

        var claimed = queue.ClaimSettled(Now.AddSeconds(301), Debounce, MaxDelay);

        Assert.Equal(userId, Assert.Single(claimed).UserId);
    }
}
