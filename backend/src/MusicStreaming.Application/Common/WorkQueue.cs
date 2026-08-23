// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Common;

/// <summary>Очередь, которую фоновый воркер разбирает по одному элементу за раз.</summary>
public interface IWorkQueue<TItem>
{
    IAsyncEnumerable<TItem> ReadAllAsync(CancellationToken cancellationToken);

    void MarkFinished(TItem item);
}

public static class WorkQueue
{
    /// <summary>
    /// Разбирает очередь до самой остановки: каждый элемент уходит в <paramref name="handle"/>,
    /// сорвавшаяся работа — в <paramref name="onError"/>, и в любом случае элемент отмечается
    /// как отработанный, иначе он навсегда останется в отсеве повторов.
    /// </summary>
    public static async Task ConsumeAsync<TItem>(
        this IWorkQueue<TItem> queue,
        Func<TItem, CancellationToken, Task> handle,
        Action<TItem, Exception> onError,
        CancellationToken stoppingToken)
    {
        await foreach (var item in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await handle(item, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                onError(item, ex);
            }
            finally
            {
                queue.MarkFinished(item);
            }
        }
    }
}
