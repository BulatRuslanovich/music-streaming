// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Collections.Concurrent;

namespace App.Recommendations.Home;

public sealed class InlineBuildGate : IDisposable
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();

    public SemaphoreSlim For(Guid userId) => _gates.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));

    public void Dispose()
    {
        foreach (var gate in _gates.Values)
            gate.Dispose();

        _gates.Clear();
    }
}
