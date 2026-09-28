// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Abstractions;

/// <param name="Vector">L2-нормированный вектор звучания.</param>
/// <param name="Windows">Сколько окон усреднено — диагностика, в БД не попадает.</param>
public record AudioEmbedding(float[] Vector, int Windows);

/// <summary>
/// Computes the sonic vector of a file. The model is required: <see cref="EnsureLoaded"/> throws
/// when it is missing or damaged, and the host does not start without it.
/// </summary>
public interface IAudioEmbedder
{
    /// <summary>Loads the model now rather than on the first track; throws if it cannot be loaded.</summary>
    void EnsureLoaded();

    /// <summary>Model identifier, stored as TrackEmbedding.ModelId.</summary>
    string ModelId { get; }

    /// <summary>Windowing strategy, stored as TrackEmbedding.Strategy.</summary>
    string Strategy { get; }

    /// <summary>Dimension of the vector <see cref="EmbedAsync"/> returns.</summary>
    int Dimension { get; }

    /// <summary>The vector of one file, or null when the file could not be decoded.</summary>
    Task<AudioEmbedding?> EmbedAsync(
        string sourceAbsolutePath,
        double durationSeconds,
        CancellationToken ct = default);
}
