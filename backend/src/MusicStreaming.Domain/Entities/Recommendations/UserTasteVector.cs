// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Entities.Recommendations;

/// <summary>
/// A listener's taste as a point in the same space the tracks live in.
/// </summary>
/// <remarks>
/// Это то, чего скалярные аффинити к артистам и жанрам дать не могут: имея такой вектор,
/// на вопрос «насколько этот трек похож на то, что я люблю» отвечает одно скалярное произведение.
/// <para>
/// Обновляется экспоненциальным скользящим средним в <c>ProfileRollupService</c> — там же, где
/// двигается watermark, поэтому каждое событие учитывается ровно один раз.
/// </para>
/// </remarks>
public class UserTasteVector
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>L2-normalised vector; empty until a single usable signal has arrived.</summary>
    public float[] Vector { get; set; } = [];

    public int Dimension { get; set; }

    /// <summary>
    /// How many positive signals this vector absorbed, undecayed.
    /// </summary>
    /// <remarks>
    /// Отличается от <see cref="UserTasteProfile.PositiveSignalMass"/> тем, что считает только
    /// события по трекам с эмбеддингом: слушатель с сотней сигналов по незаэмбежженным трекам
    /// вектора не имеет.
    /// </remarks>
    public int PositiveCount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
