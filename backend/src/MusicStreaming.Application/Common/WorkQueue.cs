// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Common;

public interface IWorkQueue<TItem>
{
    IAsyncEnumerable<TItem> ReadAllAsync(CancellationToken cancellationToken);

    void MarkFinished(TItem item);
}

public static class WorkQueue
{
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
