// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Entities.Recommendations;

/// <summary>A track's sonic vector in a learned space (CLAP).</summary>
/// <remarks>
/// Отдельная таблица, а не колонки в <see cref="TrackAudioFeatures"/>: другой производитель,
/// другой токен версии, другой режим отказа. Воркер DSP-фич переписывает свою строку целиком при
/// бампе <see cref="TrackAudioFeatures.AlgorithmVersion"/>, и это не должно уничтожать эмбеддинг,
/// который стоил секунд CPU.
/// </remarks>
public class TrackEmbedding
{
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }

    /// <summary>L2-normalised vector; empty when the analysis failed.</summary>
    public float[] Vector { get; set; } = [];

    public int Dimension { get; set; }

    /// <summary>Model identifier. Together with <see cref="Strategy"/> it is the algorithm version.</summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>How the audio was windowed before the model, e.g. "clap_3x10_v1".</summary>
    public string Strategy { get; set; } = string.Empty;

    /// <summary>
    /// <see cref="Track.ContentHash"/> as it was when the vector was computed.
    /// </summary>
    /// <remarks>
    /// Переимпорт байт-идентичного файла переиспользует вектор, а подменённый файл виден
    /// как расхождение хешей.
    /// </remarks>
    public string SourceHash { get; set; } = string.Empty;

    /// <summary>Cluster label from spherical k-means; null until clustering has run.</summary>
    public int? ClusterId { get; set; }

    public bool Succeeded { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset AnalyzedAt { get; set; }
}
