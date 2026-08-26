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
}
