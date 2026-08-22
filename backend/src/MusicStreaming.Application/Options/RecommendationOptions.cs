// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

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

    public int JustPlayedHours { get; set; } = 24;
    public int RecentlyPlayedDays { get; set; } = 7;
    public int ImpressionCooldownDays { get; set; } = 7;

    public double CollaborativeShrinkage { get; set; } = 5;
    public double CollaborativeBlendPivot { get; set; } = 10;

    public int UserCfMinUsers { get; set; } = 5;
    public int UserCfMinInteractions { get; set; } = 30;

    public int CacheTtlHours { get; set; } = 6;
    public int RegenerationDebounceSeconds { get; set; } = 60;
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
}
