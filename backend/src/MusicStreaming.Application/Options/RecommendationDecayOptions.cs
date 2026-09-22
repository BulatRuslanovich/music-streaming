// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;

namespace MusicStreaming.Application.Options;

/// <summary>How fast listening history loses weight, and when a profile counts as mature.</summary>
public class RecommendationDecayOptions
{
    public double TrackHalfLifeDays { get; set; } = 45;
    public double ArtistHalfLifeDays { get; set; } = 90;
    public double GenreHalfLifeDays { get; set; } = 90;

    /// <summary>Период полураспада массы сигналов, определяющей зрелость профиля.</summary>
    public double ProfileHalfLifeDays { get; set; } = 120;
    public double ScoreSoftness { get; set; } = 3;
    public int WarmThreshold { get; set; } = 10;
    public int MatureThreshold { get; set; } = 100;

    internal static OptionsBuilder<RecommendationOptions> Validate(
        OptionsBuilder<RecommendationOptions> builder) => builder
        .Validate(
            o => o.Decay.TrackHalfLifeDays > 0 && o.Decay.ArtistHalfLifeDays > 0 && o.Decay.GenreHalfLifeDays > 0,
            "Recommendations:Decay half-lives must be greater than zero.")
        .Validate(o => o.Decay.ProfileHalfLifeDays > 0, "Recommendations:Decay:ProfileHalfLifeDays must be greater than zero.")
        .Validate(o => o.Decay.ScoreSoftness > 0, "Recommendations:Decay:ScoreSoftness must be greater than zero.")
        .Validate(
            o => o.Decay.WarmThreshold >= 0 && o.Decay.MatureThreshold > o.Decay.WarmThreshold,
            "Recommendations:Decay:MatureThreshold must be greater than Recommendations:Decay:WarmThreshold.");
}
