// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;

namespace MusicStreaming.Application.Options;

/// <summary>
/// Multipliers and gates applied to an already-ranked candidate. Read by the pure scorer, which
/// takes this and nothing else from configuration.
/// </summary>
public class CandidatePenaltyOptions
{
    public double JustPlayed { get; set; } = 0.15;
    public double RecentlyPlayed { get; set; } = 0.60;
    public double UnclickedImpression { get; set; } = 0.50;
    public double DislikedTrack { get; set; } = 0.10;
    public double DislikedArtist { get; set; } = 0.30;

    /// <summary>С какой доли пропусков по библиотеке трек начинает считаться слабым.</summary>
    public double HighSkipRateThreshold { get; set; } = 0.50;

    /// <summary>Множитель для трека, который бросают всегда.</summary>
    public double HighSkipRatePenalty { get; set; } = 0.60;

    /// <summary>Меньше этого числа прослушиваний глобальная статистика трека не считается показательной.</summary>
    public int MinimumStatsSupport { get; set; } = 5;

    /// <summary>Нижняя граница множителя соответствия эпохе: сигнал мягкий, а не запрещающий.</summary>
    public double EraFitFloor { get; set; } = 0.75;

    /// <summary>Минимальный разброс годов, чтобы узкий профиль не отсекал всё вокруг.</summary>
    public double MinimumYearSpread { get; set; } = 6;

    public int JustPlayedHours { get; set; } = 24;
    public int RecentlyPlayedDays { get; set; } = 7;
    public int ImpressionCooldownDays { get; set; } = 7;
    public double MultiSourceBonus { get; set; } = 0.08;

    /// <summary>Сколько дней держится «не интересно» по треку. 0 — навсегда; артист блокируется навсегда всегда.</summary>
    public int TrackSuppressionDays { get; set; } = 180;

    internal static OptionsBuilder<RecommendationOptions> Validate(
        OptionsBuilder<RecommendationOptions> builder) => builder
        // Все пять — множители к оценке кандидата. Ноль вычеркнул бы трек молча, а значение выше
        // единицы превратило бы штраф в поощрение, поэтому граница та же, что у HighSkipRatePenalty.
        .Validate(o => o.Penalties.JustPlayed is > 0 and <= 1, "Recommendations:Penalties:JustPlayed must be above 0 and at most 1.")
        .Validate(o => o.Penalties.RecentlyPlayed is > 0 and <= 1, "Recommendations:Penalties:RecentlyPlayed must be above 0 and at most 1.")
        .Validate(o => o.Penalties.UnclickedImpression is > 0 and <= 1, "Recommendations:Penalties:UnclickedImpression must be above 0 and at most 1.")
        .Validate(o => o.Penalties.DislikedTrack is > 0 and <= 1, "Recommendations:Penalties:DislikedTrack must be above 0 and at most 1.")
        .Validate(o => o.Penalties.DislikedArtist is > 0 and <= 1, "Recommendations:Penalties:DislikedArtist must be above 0 and at most 1.")
        .Validate(o => o.Penalties.HighSkipRateThreshold is >= 0 and < 1, "Recommendations:Penalties:HighSkipRateThreshold must be at least 0 and below 1.")
        .Validate(o => o.Penalties.HighSkipRatePenalty is > 0 and <= 1, "Recommendations:Penalties:HighSkipRatePenalty must be above 0 and at most 1.")
        .Validate(o => o.Penalties.MinimumStatsSupport > 0, "Recommendations:Penalties:MinimumStatsSupport must be greater than zero.")
        .Validate(o => o.Penalties.EraFitFloor is > 0 and <= 1, "Recommendations:Penalties:EraFitFloor must be above 0 and at most 1.")
        .Validate(o => o.Penalties.MinimumYearSpread > 0, "Recommendations:Penalties:MinimumYearSpread must be greater than zero.")
        .Validate(o => o.Penalties.JustPlayedHours > 0, "Recommendations:Penalties:JustPlayedHours must be greater than zero.")
        .Validate(o => o.Penalties.RecentlyPlayedDays > 0, "Recommendations:Penalties:RecentlyPlayedDays must be greater than zero.")
        .Validate(o => o.Penalties.ImpressionCooldownDays > 0, "Recommendations:Penalties:ImpressionCooldownDays must be greater than zero.")
        .Validate(o => o.Penalties.MultiSourceBonus is >= 0 and <= 0.5, "Recommendations:Penalties:MultiSourceBonus must be between 0 and 0.5.")
        .Validate(o => o.Penalties.TrackSuppressionDays >= 0, "Recommendations:Penalties:TrackSuppressionDays must not be negative.");
}
