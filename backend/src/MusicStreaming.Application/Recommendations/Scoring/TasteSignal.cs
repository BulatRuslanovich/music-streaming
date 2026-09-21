// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Recommendations.Scoring;

/// <summary>
/// Знаковый вес события для векторного вкуса.
/// <para>
/// Рядом живёт <see cref="EventWeights"/>, и это не дублирование. Реестр аффинити
/// <i>накапливается и затухает</i> месяцами, поэтому ему нужны веса, складывающиеся аддитивно.
/// Вектор — экспоненциальное среднее с шагом alpha, поэтому ему нужен ограниченный рывок, где
/// пропуск это <i>направление в минус</i>, а не вычитание из счёта.
/// </para>
/// <para>
/// Две шкалы совпадают в нуле (брошенный трек: −1.0 там и здесь) и намеренно расходятся в
/// середине. <see cref="EventWeights"/> делает прослушивание на 20–50% отрицательным (−0.1),
/// здесь оно слабо положительное (+0.5·f): запись −0.1 в реестре это осмысленный толчок,
/// а сдвиг единичного вектора на −0.1·v это шум, который только добавляет дисперсию.
/// </para>
/// </summary>
public static class TasteSignal
{
    public const double LikeWeight = 2.0;
    public const double DislikeWeight = -2.0;

    /// <summary>Ниже этой доли прослушивания трек считается брошенным.</summary>
    private const double AbandonedBelow = 0.3;

    /// <summary>С этой доли трек считается дослушанным.</summary>
    private const double FinishedFrom = 0.8;

    /// <summary>
    /// Ноль означает «это событие вектор не двигает». Так, в частности, ведёт себя старт
    /// воспроизведения: иначе каждый запуск тянул бы вкус к треку ещё до того, как слушатель
    /// вынес о нём суждение, и вектор стал бы средним по истории, а не вкусом.
    /// </summary>
    public static double WeightFor(PlaybackEventType type, double completionRatio)
    {
        var fraction = Math.Clamp(completionRatio, 0, 1);

        return type switch
        {
            PlaybackEventType.TrackSkipped => fraction switch
            {
                < AbandonedBelow => -(1 - fraction),
                < FinishedFrom => 0.3 * fraction,
                _ => fraction,
            },

            // Дослушанное и просто проигранное судим одинаково: важно, сколько слушали.
            PlaybackEventType.TrackCompleted or PlaybackEventType.TrackPlayed => fraction switch
            {
                >= FinishedFrom => fraction,
                >= AbandonedBelow => 0.5 * fraction,
                _ => 0.15 * fraction,
            },

            PlaybackEventType.TrackLiked => LikeWeight,
            PlaybackEventType.TrackUnliked => DislikeWeight,

            // Повтор — сильный сигнал, но слабее осознанного лайка.
            PlaybackEventType.TrackReplayed => 1.0,

            PlaybackEventType.TrackAddedToPlaylist => 1.5,
            PlaybackEventType.TrackRemovedFromPlaylist => -1.0,
            PlaybackEventType.TrackAddedToQueue => 0.5,

            _ => 0,
        };
    }
}
