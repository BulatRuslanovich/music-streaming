// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Entities.Recommendations;

/// <summary>
/// Вектор звучания трека в выученном пространстве (CLAP). Отдельная таблица, а не колонки в
/// <see cref="TrackAudioFeatures"/>: другой производитель, другой токен версии, другой режим отказа.
/// Воркер DSP-фич переписывает свою строку целиком при бампе
/// <see cref="TrackAudioFeatures.AlgorithmVersion"/>, и это не должно уничтожать эмбеддинг,
/// который стоил секунд CPU.
/// </summary>
public class TrackEmbedding
{
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }

    /// <summary>L2-нормированный вектор; пустой, когда анализ не удался.</summary>
    public float[] Vector { get; set; } = [];

    public int Dimension { get; set; }

    /// <summary>Идентификатор модели. Вместе со <see cref="Strategy"/> играет роль версии алгоритма.</summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>Как нарезано аудио перед моделью, например "clap_3x10_v1".</summary>
    public string Strategy { get; set; } = string.Empty;

    /// <summary>
    /// <see cref="Track.ContentHash"/> на момент анализа. Переимпорт байт-идентичного файла
    /// переиспользует вектор, а подменённый файл виден как расхождение хешей.
    /// </summary>
    public string SourceHash { get; set; } = string.Empty;

    /// <summary>Метка кластера из сферического k-means; null, пока кластеризация не прошла.</summary>
    public int? ClusterId { get; set; }

    public bool Succeeded { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset AnalyzedAt { get; set; }
}
