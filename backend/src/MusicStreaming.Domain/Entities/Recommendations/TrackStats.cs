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

    /// <summary>Сколько раз трек сыграли всего.</summary>
    public int PlayCount { get; set; }

    /// <summary>Сколько раз трек сыграли за последние 30 дней — весит вдвое больше при расчёте <see cref="PopularityScore"/>, чтобы отражать текущие, а не исторические вкусы библиотеки.</summary>
    public int PlayCount30d { get; set; }

    public int SkipCount { get; set; }

    /// <summary>Сколько разных пользователей хотя бы раз включали трек.</summary>
    public int DistinctListeners { get; set; }

    /// <summary>Средняя доля трека, которую обычно дослушивают, по всей библиотеке, в [0, 1].</summary>
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

    /// <summary>Похожесть по метаданным (общий исполнитель, альбом, жанр, близкий год и длительность) — считается всегда, даже без совместных прослушиваний.</summary>
    public double ContentScore { get; set; }

    /// <summary>Похожесть по совстречаемости (общие сессии и плейлисты), с усадкой к нулю при малом <see cref="Support"/>.</summary>
    public double CollabScore { get; set; }

    /// <summary>В скольких сессиях или плейлистах пара встретилась вместе — задаёт смесь и усадку.</summary>
    public int Support { get; set; }

    public DateTimeOffset ComputedAt { get; set; }
}
