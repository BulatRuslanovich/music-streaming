// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations.Embeddings;

public interface IEmbeddingIndex
{
    bool IsReady { get; }

    EmbeddingSnapshot Snapshot();

    void RequestReload();
}
