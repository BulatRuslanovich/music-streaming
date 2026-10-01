// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Recommendations.Embeddings;

namespace Infrastructure.Recommendations;

public sealed class EmbeddingIndex : IEmbeddingIndex
{
    private volatile EmbeddingSnapshot _current = EmbeddingSnapshot.Empty;
    private volatile bool _reloadRequested;

    public bool IsReady => !_current.IsEmpty;

    public EmbeddingSnapshot Snapshot() => _current;

    public void RequestReload() => _reloadRequested = true;

    internal bool ConsumeReloadRequest()
    {
        if (!_reloadRequested)
            return false;

        _reloadRequested = false;
        return true;
    }

    internal void Publish(EmbeddingSnapshot snapshot) => _current = snapshot;
}
