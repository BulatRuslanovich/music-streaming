// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Threading.Channels;

namespace MusicStreaming.Application.Recommendations;

/// <summary>Один показ полки: трек, полка и место в ней.</summary>
public readonly record struct ImpressionItem(string ShelfKey, Guid TrackId, int Position);

public readonly record struct ImpressionBatch(
    Guid UserId, IReadOnlyList<ImpressionItem> Items, DateTimeOffset ShownAt);

/// <summary>
/// Показы полок, отложенные до фонового воркера.
/// </summary>
/// <remarks>
/// Раньше их писала прямо отдача главной страницы: выборка по уже показанному плюс до полутора
/// сотен INSERT'ов и SaveChanges — на GET самой горячей страницы. Показ не нужно засчитывать
/// синхронно: на ответ он не влияет, а на ранжирование попадёт всё равно, просто мгновением позже.
/// Переполнение очереди роняет партию молча — потерянный показ стоит дешевле задержанного ответа.
/// </remarks>
public class ImpressionQueue
{
    private const int Capacity = 1024;

    private readonly Channel<ImpressionBatch> _channel =
        Channel.CreateBounded<ImpressionBatch>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

    public bool TryEnqueue(ImpressionBatch batch) => _channel.Writer.TryWrite(batch);

    public async Task<List<ImpressionBatch>> ReadBatchAsync(
        int maxBatchSize, CancellationToken cancellationToken)
    {
        var batch = new List<ImpressionBatch>(Math.Min(maxBatchSize, 16));

        if (!await _channel.Reader.WaitToReadAsync(cancellationToken))
            return batch;

        while (batch.Count < maxBatchSize && _channel.Reader.TryRead(out var next))
            batch.Add(next);

        return batch;
    }
}
