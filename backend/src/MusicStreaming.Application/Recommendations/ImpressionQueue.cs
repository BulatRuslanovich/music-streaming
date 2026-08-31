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

    private long _accepted;
    private long _handled;

    /// <summary>Партий принято очередью.</summary>
    public long Accepted => Interlocked.Read(ref _accepted);

    /// <summary>Партий разобрано воркером — включая те, что не дошли до базы из-за ошибки.</summary>
    /// <remarks>
    /// Разница с <see cref="Accepted"/> — это то, что ещё в полёте. Снаружи это нужно тем, кто
    /// проверяет сами показы: без такой отсечки они читают таблицу раньше воркера и меряют
    /// планировщик, а не запись.
    /// </remarks>
    public long Handled => Interlocked.Read(ref _handled);

    public bool TryEnqueue(ImpressionBatch batch)
    {
        if (!_channel.Writer.TryWrite(batch))
            return false;

        Interlocked.Increment(ref _accepted);
        return true;
    }

    public void MarkHandled(int count) => Interlocked.Add(ref _handled, count);

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
