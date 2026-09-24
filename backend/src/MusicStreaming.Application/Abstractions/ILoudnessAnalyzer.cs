// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Abstractions;

public record LoudnessMeasurement(double IntegratedLufs, double TruePeakDb);

/// <summary>Integrated loudness of a whole track, used to even out volume between tracks.</summary>
/// <remarks>
/// Чтение и расчёт разведены намеренно. Замер гонит ffmpeg по всему файлу — это секунды, а на
/// большом FLAC и десятки секунд, — и раньше он жил прямо в обработчике запроса: заход на альбом
/// из двадцати треков занимал поток запроса на всё время расчёта, а процессный семафор внутри
/// ставил туда же всех остальных слушателей. Теперь запрос умеет только <see cref="CachedAsync"/>,
/// а <see cref="MeasureAsync"/> зовёт фоновый воркер.
/// </remarks>
public interface ILoudnessAnalyzer
{
    /// <summary>Whether ffmpeg is usable; without it no measurement will ever succeed.</summary>
    bool IsAvailable { get; }

    /// <summary>The measurement if it has already been made. Reads a file, never runs ffmpeg.</summary>
    Task<LoudnessMeasurement?> CachedAsync(string contentHash, CancellationToken ct = default);

    /// <summary>Measures and stores the result. Slow: decodes the whole track.</summary>
    Task<LoudnessMeasurement?> MeasureAsync(string filePath, string contentHash, CancellationToken ct = default);
}
