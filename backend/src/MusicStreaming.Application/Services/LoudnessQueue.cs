// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Collections.Concurrent;
using System.Threading.Channels;

namespace MusicStreaming.Application.Services;

/// <summary>Tracks waiting for an integrated loudness measurement.</summary>
/// <remarks>
/// Отдельно от <see cref="AudioAnalysisQueue"/> по той же причине, по какой та отделена от
/// эмбеддингов: замер декодирует запись целиком, и ставить его в одну полосу с анализом значило
/// бы, что дешёвый проход ждёт за дорогим. Дозаполнения у неё нет намеренно — громкость нужна
/// ровно тому, что слушают, и гнать ffmpeg по всей библиотеке ради треков, которые никто не
/// включал, на маленькой машине дороже, чем ждать второго прослушивания.
/// </remarks>
public class LoudnessQueue
{
    // Wait, а не DropWrite: переполненный DropWrite-канал молча выбрасывает трек, но TryWrite всё
    // равно отвечает true. Wait при переполнении честно отвечает false.
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    // Треки, которые стоят в очереди или обрабатываются прямо сейчас. Ключ держится до
    // MarkFinished, а не до выдачи: замер просят при каждом запросе нормализации, и трек, который
    // ещё меряется, иначе встал бы в очередь второй раз.
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
