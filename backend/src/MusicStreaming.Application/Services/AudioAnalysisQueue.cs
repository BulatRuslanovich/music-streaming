// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Threading.Channels;
using MusicStreaming.Application.Common;

namespace MusicStreaming.Application.Services;

public class AudioAnalysisQueue
{
    private const int Capacity = 256;

    private readonly DeduplicatingChannel<Guid, Guid> _queue =
        new(Capacity, BoundedChannelFullMode.DropWrite, trackId => trackId);

    public bool TryEnqueue(Guid trackId) => _queue.TryEnqueue(trackId);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        _queue.ReadAllAsync(cancellationToken);

    public void MarkFinished(Guid trackId) => _queue.MarkFinished(trackId);
}
