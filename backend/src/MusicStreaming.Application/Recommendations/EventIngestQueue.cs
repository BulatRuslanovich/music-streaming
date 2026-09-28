// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Threading.Channels;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Recommendations;

/// <summary>Playback events accepted by the API and waiting for EventIngestWorker to store them.</summary>
public class EventIngestQueue
{
    // Wait, а не DropWrite: переполненный DropWrite-канал молча выбрасывает событие, но TryWrite
    // всё равно отвечает true, и клиенту уходило бы «принято» за потерянное. Wait при
    // переполнении честно отвечает false, и событие попадает в Rejected.
    private readonly Channel<PlaybackEvent> _channel =
        Channel.CreateBounded<PlaybackEvent>(new BoundedChannelOptions(8192)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });

    public bool TryEnqueue(PlaybackEvent playbackEvent) => _channel.Writer.TryWrite(playbackEvent);

    public async Task<List<PlaybackEvent>> ReadBatchAsync(int maxBatchSize, CancellationToken ct)
    {
        var batch = new List<PlaybackEvent>(Math.Min(maxBatchSize, 64));

        if (!await _channel.Reader.WaitToReadAsync(ct))
            return batch;

        while (batch.Count < maxBatchSize && _channel.Reader.TryRead(out var next))
            batch.Add(next);

        return batch;
    }
}
