namespace MusicStreaming.Domain.Entities.Recommendations;

/// <summary>
/// Статистика по треку в масштабе всей библиотеки: сигналы популярности и качества, на которые
/// опирается холодный старт и которые ранжирование использует как априор для треков, ни разу не
/// слышанных этим пользователем.
/// </summary>
public class TrackStats
{
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }

    public int PlayCount { get; set; }
    public int PlayCount30d { get; set; }
    public int SkipCount { get; set; }
    public int DistinctListeners { get; set; }

    public double CompletionRate { get; set; }
    public double SkipRate { get; set; }

    /// <summary>Объём прослушиваний с поправкой на свежесть, сжатый в [0, 1].</summary>
    public double PopularityScore { get; set; }

    public DateTimeOffset? LastPlayedAt { get; set; }
    public DateTimeOffset ComputedAt { get; set; }
}

/// <summary>
/// Предрассчитанный сосед трека. Хранится в обе стороны, чтобы выборка была одним обращением к индексу.
/// </summary>
public class TrackSimilarity
{
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }

    public Guid SimilarTrackId { get; set; }
    public Track? SimilarTrack { get; set; }

    /// <summary>Смесь <see cref="ContentScore"/> и <see cref="CollabScore"/>, в диапазоне [0, 1].</summary>
    public double Score { get; set; }

    public double ContentScore { get; set; }
    public double CollabScore { get; set; }

    /// <summary>В скольких сессиях или плейлистах пара встретилась вместе — задаёт смесь и усадку.</summary>
    public int Support { get; set; }

    public DateTimeOffset ComputedAt { get; set; }
}
