// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Collections.Concurrent;

namespace App.Recommendations;

public class RecommendationRefreshQueue
{
    private readonly ConcurrentDictionary<Guid, PendingRefresh> _dirty = new();

    public void MarkDirty(Guid userId, DateTimeOffset at, bool forceRebuild = false) =>
        _dirty.AddOrUpdate(
            userId,
            new PendingRefresh(at, at, forceRebuild),
            (_, existing) => new PendingRefresh(
                existing.FirstMarkedAt <= at ? existing.FirstMarkedAt : at,
                existing.LastMarkedAt >= at ? existing.LastMarkedAt : at,
                existing.ForceRebuild || forceRebuild));

    public IReadOnlyList<RecommendationRefreshRequest> ClaimSettled(
        DateTimeOffset now, TimeSpan debounce, TimeSpan maxDelay)
    {
        var settled = new List<RecommendationRefreshRequest>();

        foreach (var (userId, pending) in _dirty)
        {
            var quiet = now - pending.LastMarkedAt >= debounce;
            var overdue = now - pending.FirstMarkedAt >= maxDelay;

            if (!quiet && !overdue)
                continue;

            if (_dirty.TryRemove(userId, out var claimed))
                settled.Add(new RecommendationRefreshRequest(userId, claimed.ForceRebuild));
        }

        return settled;
    }

    private readonly record struct PendingRefresh(
        DateTimeOffset FirstMarkedAt, DateTimeOffset LastMarkedAt, bool ForceRebuild);
}

public readonly record struct RecommendationRefreshRequest(Guid UserId, bool ForceRebuild);
