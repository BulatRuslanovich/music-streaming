// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Recommendations.Embeddings;

public sealed class EmbeddingIndex
{
    private volatile EmbeddingSnapshot _current = EmbeddingSnapshot.Empty;
    private volatile bool _reloadRequested;

    public EmbeddingSnapshot Snapshot() => _current;

    public void RequestReload() => _reloadRequested = true;

    public bool ConsumeReloadRequest()
    {
        if (!_reloadRequested)
            return false;

        _reloadRequested = false;
        return true;
    }

    public void Publish(EmbeddingSnapshot snapshot) => _current = snapshot;
}
