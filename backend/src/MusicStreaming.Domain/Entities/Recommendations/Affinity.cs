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

    public int PlayCount { get; set; }
    public int CompletedCount { get; set; }
    public int SkipCount { get; set; }
    public int ReplayCount { get; set; }
    public int QueueAdds { get; set; }
    public int PlaylistAdds { get; set; }

    public long TotalListenedSeconds { get; set; }

    /// <summary>Сумма долей дослушивания по каждому прослушиванию; делится на <see cref="CompletionSamples"/>.</summary>
    public double CompletionSum { get; set; }
    public int CompletionSamples { get; set; }

    public double DecayedWeight { get; set; }
    public DateTimeOffset DecayAnchor { get; set; }

    /// <summary>Затухший вес, сжатый в (-1, 1) на момент последнего роллапа — по нему построен индекс.</summary>
    public double Score { get; set; }

    public DateTimeOffset FirstPlayedAt { get; set; }
    public DateTimeOffset LastPlayedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

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

    public double DecayedWeight { get; set; }
    public DateTimeOffset DecayAnchor { get; set; }
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

    public double DecayedWeight { get; set; }
    public DateTimeOffset DecayAnchor { get; set; }
    public double Score { get; set; }

    public DateTimeOffset LastPlayedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
