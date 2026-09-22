// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Abstractions;

/// <param name="Vector">L2-нормированный вектор звучания.</param>
/// <param name="Windows">Сколько окон усреднено — диагностика, в БД не попадает.</param>
public record AudioEmbedding(float[] Vector, int Windows);

/// <summary>
/// Computes the sonic vector of a file. The implementation may be absent (no model, no ffmpeg);
/// then <see cref="IsAvailable"/> is false and the whole embedding path degrades the way it does
/// for an empty library rather than failing. Same contract as <see cref="IAudioTranscoder"/>
/// and <see cref="IAudioFeatureAnalyzer"/>.
/// </summary>
public interface IAudioEmbedder
{
    bool IsAvailable { get; }

    /// <summary>Model identifier, stored as TrackEmbedding.ModelId.</summary>
    string ModelId { get; }

    /// <summary>Windowing strategy, stored as TrackEmbedding.Strategy.</summary>
    string Strategy { get; }

    /// <summary>Dimension of the vector <see cref="EmbedAsync"/> returns.</summary>
    int Dimension { get; }

    Task<AudioEmbedding?> EmbedAsync(
        string sourceAbsolutePath,
        double durationSeconds,
        CancellationToken ct = default);
}
