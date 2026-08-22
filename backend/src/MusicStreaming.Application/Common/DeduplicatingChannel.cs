// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Collections.Concurrent;
using System.Threading.Channels;

namespace MusicStreaming.Application.Common;

internal sealed class DeduplicatingChannel<TItem, TKey>
    where TKey : notnull
{
    private readonly Channel<TItem> _channel;
    private readonly ConcurrentDictionary<TKey, byte> _pending;
    private readonly Func<TItem, TKey> _keyOf;

    public DeduplicatingChannel(
        int capacity,
        BoundedChannelFullMode fullMode,
        Func<TItem, TKey> keyOf,
        IEqualityComparer<TKey>? comparer = null)
    {
        _channel = Channel.CreateBounded<TItem>(new BoundedChannelOptions(capacity)
        {
            FullMode = fullMode,
            SingleReader = true,
        });
        _pending = new ConcurrentDictionary<TKey, byte>(comparer ?? EqualityComparer<TKey>.Default);
        _keyOf = keyOf;
    }

    public bool TryEnqueue(TItem item)
    {
        var key = _keyOf(item);
        if (!_pending.TryAdd(key, 0))
            return false;

        if (_channel.Writer.TryWrite(item))
            return true;

        _pending.TryRemove(key, out _);
        return false;
    }

    public IAsyncEnumerable<TItem> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public void MarkFinished(TItem item) => _pending.TryRemove(_keyOf(item), out _);
}
