// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace Infrastructure.Security;

// Каждый запрос с access-токеном проверяет, что его сессия ("sid") ещё жива, а это запрос в базу на
// каждый HLS-сегмент и обложку. Живые сессии запоминаются ненадолго; отзыв сбрасывает их сразу.
// Запрос, прочитавший базу до отзыва, может запомнить сессию уже после него, но не дольше Lifetime.
public sealed class LiveSessionCache(IMemoryCache cache)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _byUser = new();

    public bool IsKnownAlive(Guid sessionId) => cache.TryGetValue(Key(sessionId), out _);

    public void RememberAlive(Guid sessionId, Guid userId)
    {
        var revoked = _byUser.GetOrAdd(userId, _ => new CancellationTokenSource());

        var options = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = Lifetime }
            .AddExpirationToken(new CancellationChangeToken(revoked.Token));

        cache.Set(Key(sessionId), true, options);
    }

    public void Forget(Guid sessionId) => cache.Remove(Key(sessionId));

    public void ForgetUser(Guid userId)
    {
        if (!_byUser.TryRemove(userId, out var revoked))
            return;

        revoked.Cancel();
        revoked.Dispose();
    }

    private static string Key(Guid sessionId) => $"live-session:{sessionId:N}";
}
