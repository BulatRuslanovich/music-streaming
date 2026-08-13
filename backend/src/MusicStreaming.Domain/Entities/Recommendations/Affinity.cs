namespace MusicStreaming.Domain.Entities.Recommendations;

/// <summary>
/// Насколько пользователю нравится один трек — накоплено по всем событиям этой пары.
///
/// <para>
/// <see cref="DecayedWeight"/> и <see cref="DecayAnchor"/> реализуют экспоненциальное затухание
/// инкрементально: добавление веса <c>w</c> в момент <c>t</c> — это
/// <c>weight = weight * 2^(-(t - anchor)/halfLife) + w; anchor = t</c>, а значение в любой
/// последующий момент — <c>weight * 2^(-(now - anchor)/halfLife)</c>. За счёт этого роллап
/// стоит O(1) на событие, а сырые события можно удалять, не теряя накопленный вкус.
/// </para>
/// </summary>
public class UserTrackAffinity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid TrackId { get; set; }
    public Track? Track { get; set; }

    /// <summary>Сколько раз трек был запущен (учитывается на <see cref="PlaybackEventType.TrackCompleted"/> и <see cref="PlaybackEventType.TrackSkipped"/>).</summary>
    public int PlayCount { get; set; }

    /// <summary>Сколько раз трек был дослушан до конца.</summary>
    public int CompletedCount { get; set; }

    /// <summary>Сколько раз трек был брошен в начале — доля дослушивания ниже порога "скип".</summary>
    public int SkipCount { get; set; }
    public int ReplayCount { get; set; }
    public int QueueAdds { get; set; }
    public int PlaylistAdds { get; set; }

    public long TotalListenedSeconds { get; set; }

    /// <summary>Сумма долей дослушивания по каждому прослушиванию; делится на <see cref="CompletionSamples"/>.</summary>
    public double CompletionSum { get; set; }

    /// <summary>Число прослушиваний, учтённых в <see cref="CompletionSum"/> — знаменатель для <see cref="AverageCompletion"/>.</summary>
    public int CompletionSamples { get; set; }

    /// <summary>Накопленный вес с экспоненциальным затуханием, актуальный на момент <see cref="DecayAnchor"/> — см. формулу в доке класса.</summary>
    public double DecayedWeight { get; set; }

    /// <summary>Момент, на который актуален <see cref="DecayedWeight"/>.</summary>
    public DateTimeOffset DecayAnchor { get; set; }

    /// <summary>Затухший вес, сжатый в (-1, 1) на момент последнего роллапа — по нему построен индекс.</summary>
    public double Score { get; set; }

    public DateTimeOffset FirstPlayedAt { get; set; }
    public DateTimeOffset LastPlayedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Средняя доля трека, которая обычно дослушивается пользователем, в [0, 1].</summary>
    public double AverageCompletion => CompletionSamples == 0 ? 0 : CompletionSum / CompletionSamples;
}

/// <summary>Вкус к одному исполнителю, накапливается так же, как <see cref="UserTrackAffinity"/>.</summary>
public class UserArtistAffinity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid ArtistId { get; set; }
    public Artist? Artist { get; set; }

    public int PlayCount { get; set; }
    public int SkipCount { get; set; }

    /// <summary>Накопленный вес с экспоненциальным затуханием, актуальный на момент <see cref="DecayAnchor"/>.</summary>
    public double DecayedWeight { get; set; }

    /// <summary>Момент, на который актуален <see cref="DecayedWeight"/>.</summary>
    public DateTimeOffset DecayAnchor { get; set; }

    /// <summary>Затухший вес, сжатый в (-1, 1) — по нему ранжирование сравнивает исполнителей между собой.</summary>
    public double Score { get; set; }

    public DateTimeOffset LastPlayedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Вкус к одному жанру, накапливается так же, как <see cref="UserTrackAffinity"/>.</summary>
public class UserGenreAffinity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid GenreId { get; set; }
    public Genre? Genre { get; set; }

    public int PlayCount { get; set; }
    public int SkipCount { get; set; }

    /// <summary>Накопленный вес с экспоненциальным затуханием, актуальный на момент <see cref="DecayAnchor"/>.</summary>
    public double DecayedWeight { get; set; }

    /// <summary>Момент, на который актуален <see cref="DecayedWeight"/>.</summary>
    public DateTimeOffset DecayAnchor { get; set; }

    /// <summary>Затухший вес, сжатый в (-1, 1) — по нему ранжирование сравнивает жанры между собой.</summary>
    public double Score { get; set; }

    public DateTimeOffset LastPlayedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
