// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Threading.Channels;
using MusicStreaming.Application.Common;

namespace MusicStreaming.Application.Services;

/// <summary>
/// Очередь идентификаторов треков на фоновую обработку: переполнение отбрасывает новое, а не
/// вытесняет принятое, и один трек не стоит в ней дважды. Наследники различаются только тем,
/// какую работу представляют, — тип нужен контейнеру, чтобы развести воркеров.
/// </summary>
public abstract class TrackWorkQueue : IWorkQueue<Guid>
{
    private const int Capacity = 256;

    private readonly DeduplicatingChannel<Guid, Guid> _queue =
        new(Capacity, BoundedChannelFullMode.DropWrite, trackId => trackId);

    public bool TryEnqueue(Guid trackId) => _queue.TryEnqueue(trackId);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) =>
        _queue.ReadAllAsync(ct);

    public void MarkFinished(Guid trackId) => _queue.MarkFinished(trackId);
}

public class AudioAnalysisQueue : TrackWorkQueue;
