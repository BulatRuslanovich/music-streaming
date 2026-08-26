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

    /// <summary>
    /// Активность откладывает пересборку: держим последнюю метку, а не самую раннюю. Первая метка
    /// остаётся как потолок задержки, иначе у человека, который слушает часами подряд, полки не
    /// обновились бы ни разу.
    /// </summary>
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
