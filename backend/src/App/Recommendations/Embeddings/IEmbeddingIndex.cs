// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Recommendations.Embeddings;

public interface IEmbeddingIndex
{
    bool IsReady { get; }

    EmbeddingSnapshot Snapshot();

    void RequestReload();
}
