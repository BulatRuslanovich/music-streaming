// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Entities.Recommendations;

/// <summary>
/// В каком контексте накоплен вектор вкуса: общий и по одному на каждую часть суток.
/// Значения намеренно смещены на единицу относительно <see cref="Daypart"/>, чтобы ноль
/// означал «общий», а не «утро».
/// </summary>
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
/// Вкус слушателя как точка в том же пространстве, где лежат треки. Это то, чего скалярные
/// аффинити к артистам и жанрам дать не могут: имея такой вектор, на вопрос «насколько этот
/// трек похож на то, что я люблю» отвечает одно скалярное произведение.
/// <para>
/// Обновляется экспоненциальным скользящим средним в <c>ProfileRollupService</c> — там же, где
/// двигается watermark, поэтому каждое событие учитывается ровно один раз.
/// </para>
/// </summary>
public class UserTasteVector
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public TasteContext Context { get; set; }

    /// <summary>L2-нормированный вектор; пустой, пока не было ни одного пригодного сигнала.</summary>
    public float[] Vector { get; set; } = [];

    public int Dimension { get; set; }

    /// <summary>
    /// Сколько положительных сигналов впитал именно этот вектор — без затухания. Отличается от
    /// <see cref="UserTasteProfile.PositiveSignalMass"/> тем, что считает только события по трекам
    /// с эмбеддингом: слушатель с сотней сигналов по незаэмбежженным трекам вектора не имеет.
    /// </summary>
    public int PositiveCount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
