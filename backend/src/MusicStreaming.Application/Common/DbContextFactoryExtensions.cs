// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Abstractions;

namespace MusicStreaming.Application.Common;

public static class DbContextFactoryExtensions
{
    /// <summary>Выполняет выборку на собственном контексте — их нельзя делить между потоками.</summary>
    public static async Task<T> QueryAsync<T>(
        this IApplicationDbContextFactory factory, Func<IApplicationDbContext, Task<T>> query)
    {
        var scoped = factory.Create();

        try
        {
            return await query(scoped);
        }
        finally
        {
            if (scoped is IAsyncDisposable disposable)
                await disposable.DisposeAsync();
        }
    }
}
