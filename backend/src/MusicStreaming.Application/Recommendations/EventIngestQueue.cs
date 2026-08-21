// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Threading.Channels;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Recommendations;

public class EventIngestQueue
{
    private const int Capacity = 8192;

    private readonly Channel<PlaybackEvent> _channel =
        Channel.CreateBounded<PlaybackEvent>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

    private long _dropped;

    public bool TryEnqueue(PlaybackEvent playbackEvent)
    {
        if (_channel.Writer.TryWrite(playbackEvent))
            return true;

        Interlocked.Increment(ref _dropped);
        return false;
    }

    public async Task<List<PlaybackEvent>> ReadBatchAsync(int maxBatchSize, CancellationToken cancellationToken)
    {
        var batch = new List<PlaybackEvent>(Math.Min(maxBatchSize, 64));

        if (!await _channel.Reader.WaitToReadAsync(cancellationToken))
            return batch;

        while (batch.Count < maxBatchSize && _channel.Reader.TryRead(out var next))
            batch.Add(next);

        return batch;
    }
}

public class RecommendationRefreshQueue
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, PendingRefresh> _dirty = new();

    public void MarkDirty(Guid userId, DateTimeOffset at, bool forceRebuild = false) =>
        _dirty.AddOrUpdate(
            userId,
            new PendingRefresh(at, forceRebuild),
            (_, existing) => new PendingRefresh(
                existing.MarkedAt <= at ? existing.MarkedAt : at,
                existing.ForceRebuild || forceRebuild));

    public IReadOnlyList<RecommendationRefreshRequest> ClaimSettled(DateTimeOffset now, TimeSpan debounce)
    {
        var settled = new List<RecommendationRefreshRequest>();

        foreach (var (userId, pending) in _dirty)
        {
            if (now - pending.MarkedAt < debounce)
                continue;

            if (_dirty.TryRemove(userId, out var claimed))
                settled.Add(new RecommendationRefreshRequest(userId, claimed.ForceRebuild));
        }

        return settled;
    }

    private readonly record struct PendingRefresh(DateTimeOffset MarkedAt, bool ForceRebuild);
}

public readonly record struct RecommendationRefreshRequest(Guid UserId, bool ForceRebuild);
