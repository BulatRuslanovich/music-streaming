// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Entities.Recommendations;

/// <summary>
/// Which context a taste vector was accumulated in: one global, one per part of the day.
/// </summary>
/// <remarks>
/// Значения намеренно смещены на единицу относительно <see cref="Daypart"/>, чтобы ноль
/// означал «общий», а не «утро».
/// </remarks>
public enum TasteContext
{
    Global = 0,
    Morning = 1,
    Day = 2,
    Evening = 3,
    Night = 4,
}

public static class TasteContexts
{
    public static TasteContext For(Daypart part) => (TasteContext)((int)part + 1);

    public static readonly IReadOnlyList<TasteContext> All =
    [
        TasteContext.Global,
        TasteContext.Morning,
        TasteContext.Day,
        TasteContext.Evening,
        TasteContext.Night,
    ];
}

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
    public TasteContext Context { get; set; }

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
