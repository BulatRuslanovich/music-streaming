// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;
using MusicStreaming.Application.Recommendations.Scoring;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Options;

public class RecommendationOptions
{
    public const string SectionName = "Recommendations";
    public bool Enabled { get; set; } = true;
    public double TrackHalfLifeDays { get; set; } = 45;
    public double ArtistHalfLifeDays { get; set; } = 90;
    public double GenreHalfLifeDays { get; set; } = 90;

    /// <summary>Период полураспада массы сигналов, определяющей зрелость профиля.</summary>
    public double ProfileHalfLifeDays { get; set; } = 120;
    public double ScoreSoftness { get; set; } = 3;
    public double FreshnessWindowDays { get; set; } = 30;
    public int WarmThreshold { get; set; } = 10;
    public int MatureThreshold { get; set; } = 100;

    public RankingWeights Cold { get; set; } = RankingWeights.ColdDefaults();
    public RankingWeights Warm { get; set; } = RankingWeights.WarmDefaults();
    public RankingWeights Mature { get; set; } = RankingWeights.MatureDefaults();

    public int ShelfSize { get; set; } = 12;
    public int CandidateLimit { get; set; } = 600;
    public int PerSourceLimit { get; set; } = 120;

    public int SimilarTopK { get; set; } = 50;
    public double ExplorationRatio { get; set; } = 0.25;
    public double DiscoveryExplorationRatio { get; set; } = 0.60;
    public double DiversityLambda { get; set; } = 0.30;
    public double MultiSourceBonus { get; set; } = 0.08;

    public int MaxPerArtist { get; set; } = 2;
    public int MaxPerAlbum { get; set; } = 2;
    public int MaxPerGenre { get; set; } = 4;

    public double JustPlayedPenalty { get; set; } = 0.15;
    public double RecentlyPlayedPenalty { get; set; } = 0.60;
    public double UnclickedImpressionPenalty { get; set; } = 0.50;
    public double DislikedTrackPenalty { get; set; } = 0.10;
    public double DislikedArtistPenalty { get; set; } = 0.30;

    /// <summary>Сколько дней держится «не интересно» по треку. 0 — навсегда; артист блокируется навсегда всегда.</summary>
    public int TrackSuppressionDays { get; set; } = 180;

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

    /// <summary>За сколько дней собирается вкус по частям суток.</summary>
    public int DaypartWindowDays { get; set; } = 90;

    /// <summary>Ниже этой доли прослушивания часть суток не заслуживает собственной полки.</summary>
    public double MinimumDaypartShare { get; set; } = 0.10;

    public int JustPlayedHours { get; set; } = 24;
    public int RecentlyPlayedDays { get; set; } = 7;
    public int ImpressionCooldownDays { get; set; } = 7;

    public double CollaborativeShrinkage { get; set; } = 5;
    public double CollaborativeBlendPivot { get; set; } = 10;

    public int UserCfMinUsers { get; set; } = 5;
    public int UserCfMinInteractions { get; set; } = 30;

    public int CacheTtlHours { get; set; } = 6;
    public int RegenerationDebounceSeconds { get; set; } = 60;

    /// <summary>Потолок задержки пересборки: непрерывная активность не должна откладывать её вечно.</summary>
    public int RegenerationMaxDelaySeconds { get; set; } = 300;
    public int SimilarityIntervalHours { get; set; } = 6;
    public int StartupDelaySeconds { get; set; } = 30;
    public int EventRetentionDays { get; set; } = 180;
    public int ImpressionRetentionDays { get; set; } = 60;

    public int MaxEventsPerRequest { get; set; } = 100;

    public RankingWeights WeightsFor(ProfileMaturity maturity) => maturity switch
    {
        ProfileMaturity.Mature => Mature,
        ProfileMaturity.Warm => Warm,
        _ => Cold,
    };

    public static OptionsBuilder<RecommendationOptions> Validated(
        OptionsBuilder<RecommendationOptions> builder) => builder
        .Validate(o => o.TrackHalfLifeDays > 0 && o.ArtistHalfLifeDays > 0 && o.GenreHalfLifeDays > 0, "Recommendations half-lives must be greater than zero.")
        .Validate(o => o.ProfileHalfLifeDays > 0, "Recommendations:ProfileHalfLifeDays must be greater than zero.")
        .Validate(o => o.HighSkipRateThreshold is >= 0 and < 1, "Recommendations:HighSkipRateThreshold must be at least 0 and below 1.")
        .Validate(o => o.HighSkipRatePenalty is > 0 and <= 1, "Recommendations:HighSkipRatePenalty must be above 0 and at most 1.")
        .Validate(o => o.MinimumStatsSupport > 0, "Recommendations:MinimumStatsSupport must be greater than zero.")
        .Validate(o => o.EraFitFloor is > 0 and <= 1, "Recommendations:EraFitFloor must be above 0 and at most 1.")
        .Validate(o => o.MinimumYearSpread > 0, "Recommendations:MinimumYearSpread must be greater than zero.")
        .Validate(o => o.ScoreSoftness > 0, "Recommendations:ScoreSoftness must be greater than zero.")
        .Validate(o => o.WarmThreshold >= 0 && o.MatureThreshold > o.WarmThreshold, "Recommendations:MatureThreshold must be greater than Recommendations:WarmThreshold.")
        .Validate(o => o.ShelfSize > 0, "Recommendations:ShelfSize must be greater than zero.")
        .Validate(o => o.CandidateLimit >= o.ShelfSize, "Recommendations:CandidateLimit must be at least Recommendations:ShelfSize.")
        .Validate(
            o => o.RegenerationMaxDelaySeconds >= o.RegenerationDebounceSeconds,
            "Recommendations:RegenerationMaxDelaySeconds must be at least Recommendations:RegenerationDebounceSeconds.")
        .Validate(o => o.TrackSuppressionDays >= 0, "Recommendations:TrackSuppressionDays must not be negative.")
        .Validate(o => o.DaypartWindowDays > 0, "Recommendations:DaypartWindowDays must be positive.")
        .Validate(
            o => o.MinimumDaypartShare is >= 0 and <= 1,
            "Recommendations:MinimumDaypartShare must be between 0 and 1.")
        .Validate(o => o.ExplorationRatio is >= 0 and <= 1, "Recommendations:ExplorationRatio must be between 0 and 1.")
        .Validate(o => o.DiscoveryExplorationRatio is >= 0 and <= 1, "Recommendations:DiscoveryExplorationRatio must be between 0 and 1.")
        .Validate(o => o.DiversityLambda is >= 0 and < 1, "Recommendations:DiversityLambda must be at least 0 and below 1.")
        .Validate(o => o.MultiSourceBonus is >= 0 and <= 0.5, "Recommendations:MultiSourceBonus must be between 0 and 0.5.")
        .Validate(o => o.MaxPerArtist > 0 && o.MaxPerAlbum > 0 && o.MaxPerGenre > 0, "Recommendations per-shelf caps must be greater than zero.")
        .Validate(o => o.CacheTtlHours > 0, "Recommendations:CacheTtlHours must be greater than zero.")
        .Validate(o => o.EventRetentionDays > 0, "Recommendations:EventRetentionDays must be greater than zero.")
        .Validate(o => o.MaxEventsPerRequest > 0, "Recommendations:MaxEventsPerRequest must be greater than zero.");
}
