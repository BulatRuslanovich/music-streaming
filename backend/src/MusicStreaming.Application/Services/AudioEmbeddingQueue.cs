// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Collections.Concurrent;
using System.Threading.Channels;

namespace MusicStreaming.Application.Services;

public class AudioEmbeddingQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    private readonly ConcurrentDictionary<Guid, byte> _queued = new();

    public bool TryEnqueue(Guid trackId)
    {
        if (!_queued.TryAdd(trackId, 0))
            return false;

        if (_channel.Writer.TryWrite(trackId))
            return true;

        _queued.TryRemove(trackId, out _);
        return false;
    }

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);

    public void MarkFinished(Guid trackId) => _queued.TryRemove(trackId, out _);
}
