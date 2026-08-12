using MusicStreaming.Application.Recommendations.Scoring;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Options;

/// <summary>
/// Все настраиваемые параметры движка рекомендаций, привязанные к секции конфигурации
/// <c>Recommendations</c>. Значения по умолчанию здесь — это поведение «из коробки»; любое из них
/// оператор может перенастроить через appsettings или переменные окружения, не трогая код.
/// </summary>
public class RecommendationOptions
{
    public const string SectionName = "Recommendations";

    /// <summary>Выключает фоновые воркеры. Чтение продолжает работать и откатывается к холодному старту.</summary>
    public bool Enabled { get; set; } = true;

    // ── Затухание ───────────────────────────────────────────────────────────────────────────
    /// <summary>Аффинити к треку уменьшается вдвое за столько дней.</summary>
    public double TrackHalfLifeDays { get; set; } = 45;

    /// <summary>Вкус к исполнителю и жанру меняется медленнее, чем вкус к отдельному треку.</summary>
    public double ArtistHalfLifeDays { get; set; } = 90;
    public double GenreHalfLifeDays { get; set; } = 90;

    /// <summary>Какой накопленный вес считается сильным предпочтением — см. AffinityMath.</summary>
    public double ScoreSoftness { get; set; } = 3;

    /// <summary>За сколько дней только что добавленный трек перестаёт считаться свежим.</summary>
    public double FreshnessWindowDays { get; set; } = 30;

    // ── Зрелость профиля ────────────────────────────────────────────────────────────────────
    public int WarmThreshold { get; set; } = 10;
    public int MatureThreshold { get; set; } = 100;

    public RankingWeights Cold { get; set; } = RankingWeights.ColdDefaults();
    public RankingWeights Warm { get; set; } = RankingWeights.WarmDefaults();
    public RankingWeights Mature { get; set; } = RankingWeights.MatureDefaults();

    // ── Полки ───────────────────────────────────────────────────────────────────────────────
    public int ShelfSize { get; set; } = 12;
    public int CandidateLimit { get; set; } = 600;
    public int PerSourceLimit { get; set; } = 120;

    /// <summary>Сколько соседей хранится на трек.</summary>
    public int SimilarTopK { get; set; } = 50;

    /// <summary>Доля слотов, отдаваемая кандидатам вне устоявшегося вкуса пользователя.</summary>
    public double ExplorationRatio { get; set; } = 0.25;

    /// <summary>Полка «вам может понравиться» существует ради исследования, поэтому баланс обратный.</summary>
    public double DiscoveryExplorationRatio { get; set; } = 0.60;

    /// <summary>Насколько сильно переранжирование меняет оценку на разнообразие, в [0, 1).</summary>
    public double DiversityLambda { get; set; } = 0.30;

    public int MaxPerArtist { get; set; } = 2;
    public int MaxPerAlbum { get; set; } = 2;
    public int MaxPerGenre { get; set; } = 4;

    // ── Штрафы ──────────────────────────────────────────────────────────────────────────────
    public double JustPlayedPenalty { get; set; } = 0.15;
    public double RecentlyPlayedPenalty { get; set; } = 0.60;
    public double UnclickedImpressionPenalty { get; set; } = 0.50;
    public double DislikedTrackPenalty { get; set; } = 0.10;
    public double DislikedArtistPenalty { get; set; } = 0.30;

    public int JustPlayedHours { get; set; } = 24;
    public int RecentlyPlayedDays { get; set; } = 7;
    public int ImpressionCooldownDays { get; set; } = 7;

    // ── Похожесть ───────────────────────────────────────────────────────────────────────────
    /// <summary>Совстречаемость ниже этого значения остаётся подтянутой к нулю.</summary>
    public double CollaborativeShrinkage { get; set; } = 5;

    /// <summary>Объём свидетельств, при котором коллаборативная половина похожести весит наравне.</summary>
    public double CollaborativeBlendPivot { get; set; } = 10;

    // ── Коллаборативная фильтрация между пользователями ─────────────────────────────────────
    /// <summary>
    /// Межпользовательские рекомендации выключены, пока слушателей не станет достаточно, чтобы
    /// статистика хоть что-то значила. Ниже этих порогов источник соседей не возвращает ничего, а
    /// результат целиком несут item-item и контентный пути.
    /// </summary>
    public int UserCfMinUsers { get; set; } = 5;
    public int UserCfMinInteractions { get; set; } = 30;

    // ── Расписание и ретеншн ────────────────────────────────────────────────────────────────
    public int CacheTtlHours { get; set; } = 6;
    public int RegenerationDebounceSeconds { get; set; } = 60;
    public int SimilarityIntervalHours { get; set; } = 6;
    public int StartupDelaySeconds { get; set; } = 30;
    public int EventRetentionDays { get; set; } = 180;
    public int ImpressionRetentionDays { get; set; } = 60;

    /// <summary>Сколько событий принимается за один запрос.</summary>
    public int MaxEventsPerRequest { get; set; } = 100;

    public RankingWeights WeightsFor(ProfileMaturity maturity) => maturity switch
    {
        ProfileMaturity.Mature => Mature,
        ProfileMaturity.Warm => Warm,
        _ => Cold,
    };

    public TimeSpan TrackHalfLife => TimeSpan.FromDays(TrackHalfLifeDays);
}
