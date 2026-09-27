// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Collections.Concurrent;
using MusicStreaming.Application.Common;

namespace MusicStreaming.Application.Services;

/// <param name="lockoutAttempts">0 turns the lock off; only tests ever pass anything but the default.</param>
public class LoginAttemptTracker(
    TimeProvider clock,
    int lockoutAttempts = SecurityLimits.AccountLockoutAttempts,
    int lockoutMinutes = SecurityLimits.AccountLockoutMinutes)
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

        var now = clock.GetUtcNow();
        var window = TimeSpan.FromMinutes(lockoutMinutes);

        _byUsername.AddOrUpdate(
            username,
            _ => new Attempts(1, DateTimeOffset.MinValue, now),
            (_, previous) =>
            {
                var failures = now - previous.LastFailureAt > window ? 1 : previous.Failures + 1;

                return new Attempts(
                    failures,
                    failures >= lockoutAttempts ? now + window : previous.LockedUntil,
                    now);
            });

        Prune(now, window);
    }

    public void RecordSuccess(string username) => _byUsername.TryRemove(username, out _);

    private bool Enabled => lockoutAttempts > 0;

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
