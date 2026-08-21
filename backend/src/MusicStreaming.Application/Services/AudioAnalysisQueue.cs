// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Collections.Concurrent;
using System.Threading.Channels;

namespace MusicStreaming.Application.Services;

public class AudioAnalysisQueue
{
    private const int Capacity = 256;

    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(Capacity)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });

    private readonly ConcurrentDictionary<Guid, byte> _pending = [];

    public bool TryEnqueue(Guid trackId)
    {
        if (!_pending.TryAdd(trackId, 0))
            return false;

        if (_channel.Writer.TryWrite(trackId))
            return true;

        _pending.TryRemove(trackId, out _);
        return false;
    }

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public void MarkFinished(Guid trackId) => _pending.TryRemove(trackId, out _);
}
