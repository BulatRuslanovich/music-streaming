// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;

namespace MusicStreaming.Application.Options;

/// <summary>
/// How much repetition one shelf or one queue may contain. Read by the pure MMR selection, which
/// takes this and nothing else from configuration.
/// </summary>
public class DiversityOptions
{
    public int MaxPerArtist { get; set; } = 2;
    public int MaxPerAlbum { get; set; } = 2;
    public int MaxPerGenre { get; set; } = 4;
    public double DiversityLambda { get; set; } = 0.30;

    /// <summary>
    /// Накопительный штраф в MMR за каждое предыдущее появление артиста. Основную работу делает
    /// на последней ступени послаблений, где жёстких лимитов уже нет.
    /// </summary>
    public double ArtistRepeatPenalty { get; set; } = 0.15;

    internal static OptionsBuilder<RecommendationOptions> Validate(
        OptionsBuilder<RecommendationOptions> builder) => builder
        .Validate(
            o => o.Diversity.MaxPerArtist > 0 && o.Diversity.MaxPerAlbum > 0 && o.Diversity.MaxPerGenre > 0,
            "Recommendations:Diversity caps must be greater than zero.")
        .Validate(
            o => o.Diversity.DiversityLambda is >= 0 and < 1,
            "Recommendations:Diversity:DiversityLambda must be at least 0 and below 1.")
        .Validate(
            o => o.Diversity.ArtistRepeatPenalty is >= 0 and <= 1,
            "Recommendations:Diversity:ArtistRepeatPenalty must be in [0, 1].");
}
