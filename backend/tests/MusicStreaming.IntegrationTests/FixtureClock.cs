// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.IntegrationTests;

public sealed class FixtureClock : TimeProvider
{
    private DateTimeOffset? _pinned;

    public override DateTimeOffset GetUtcNow() => _pinned ?? base.GetUtcNow();

    public IDisposable PinnedAt(DateTimeOffset moment)
    {
        _pinned = moment;
        return new Release(this);
    }

    private sealed class Release(FixtureClock clock) : IDisposable
    {
        public void Dispose() => clock._pinned = null;
    }
}
