// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Abstractions;

public record AudioEmbedding(float[] Vector, int Windows);

public interface IAudioEmbedder
{
    void EnsureLoaded();

    string ModelId { get; }

    string Strategy { get; }

    int Dimension { get; }

    Task<AudioEmbedding?> EmbedAsync(
        string sourceAbsolutePath,
        double durationSeconds,
        CancellationToken ct = default);
}
