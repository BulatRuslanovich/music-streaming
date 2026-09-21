// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Abstractions;

/// <param name="Vector">L2-нормированный вектор звучания.</param>
/// <param name="Windows">Сколько окон усреднено — диагностика, в БД не попадает.</param>
public record AudioEmbedding(float[] Vector, int Windows);

/// <summary>
/// Считает вектор звучания файла. Реализация может отсутствовать (нет модели, нет ffmpeg) —
/// тогда <see cref="IsAvailable"/> равно false и весь путь эмбеддингов деградирует так же,
/// как при пустой библиотеке, а не падает. Тот же контракт, что у
/// <see cref="IAudioTranscoder"/> и <see cref="IAudioFeatureAnalyzer"/>.
/// </summary>
public interface IAudioEmbedder
{
    bool IsAvailable { get; }

    /// <summary>Идентификатор модели, попадающий в TrackEmbedding.ModelId.</summary>
    string ModelId { get; }

    /// <summary>Стратегия нарезки, попадающая в TrackEmbedding.Strategy.</summary>
    string Strategy { get; }

    /// <summary>Размерность вектора, который вернёт <see cref="EmbedAsync"/>.</summary>
    int Dimension { get; }

    Task<AudioEmbedding?> EmbedAsync(
        string sourceAbsolutePath,
        double durationSeconds,
        CancellationToken cancellationToken = default);
}
