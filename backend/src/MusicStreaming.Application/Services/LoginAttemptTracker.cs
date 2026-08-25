// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Options;

namespace MusicStreaming.Application.Services;

public class LoginAttemptTracker(IOptions<SecurityOptions> options, TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, Attempts> _byUsername = new(StringComparer.Ordinal);

    private sealed record Attempts(int Failures, DateTimeOffset LockedUntil, DateTimeOffset LastFailureAt);

    public TimeSpan? LockoutRemaining(string username)
    {
        if (!Enabled || !_byUsername.TryGetValue(username, out var attempts))
            return null;

        var remaining = attempts.LockedUntil - clock.GetUtcNow();
        return remaining > TimeSpan.Zero ? remaining : null;
    }

    public void RecordFailure(string username)
    {
        if (!Enabled)
            return;

        var settings = options.Value;
        var now = clock.GetUtcNow();
        var window = TimeSpan.FromMinutes(settings.AccountLockoutMinutes);

        _byUsername.AddOrUpdate(
            username,
            _ => new Attempts(1, DateTimeOffset.MinValue, now),
            (_, previous) =>
            {
                var failures = now - previous.LastFailureAt > window ? 1 : previous.Failures + 1;

                return new Attempts(
                    failures,
                    failures >= settings.AccountLockoutAttempts ? now + window : previous.LockedUntil,
                    now);
            });

        Prune(now, window);
    }

    public void RecordSuccess(string username) => _byUsername.TryRemove(username, out _);

    private bool Enabled => options.Value.AccountLockoutAttempts > 0;

    private void Prune(DateTimeOffset now, TimeSpan window)
    {
        if (_byUsername.Count < PruneThreshold)
            return;

        foreach (var (username, attempts) in _byUsername)
        {
            if (now - attempts.LastFailureAt > window && attempts.LockedUntil <= now)
                _byUsername.TryRemove(username, out _);
        }
    }

    private const int PruneThreshold = 1000;
}
