// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Collections.Concurrent;
using System.Threading.Channels;

namespace MusicStreaming.Application.Services;

/// <summary>Tracks waiting for a CLAP embedding.</summary>
/// <remarks>
/// Переполнение не страшно: дозаполнение в AudioEmbeddingWorker подберёт трек на следующем проходе.
/// </remarks>
public class AudioEmbeddingQueue
{
    // Wait, а не DropWrite: переполненный DropWrite-канал молча выбрасывает трек, но TryWrite всё
    // равно отвечает true. Wait при переполнении честно отвечает false.
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    // Треки, которые стоят в очереди или обрабатываются прямо сейчас. Ключ держится до
    // MarkFinished, а не до выдачи: дозаполнение регулярно ставит заново всё, у чего нет вектора,
    // и трек, над которым воркер ещё работает, иначе встал бы в очередь второй раз.
    private readonly ConcurrentDictionary<Guid, byte> _queued = new();

    /// <summary>Queues a track. False when it is already queued or the queue is full.</summary>
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

    /// <summary>The worker is done with the track, so it may be queued again.</summary>
    public void MarkFinished(Guid trackId) => _queued.TryRemove(trackId, out _);
}
