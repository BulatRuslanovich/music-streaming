namespace MusicStreaming.Domain.Entities.Recommendations;

/// <summary>Одна запись сохранённого топ-N (исполнителя или жанра).</summary>
public record TasteEntry(Guid Id, string Name, double Score);

/// <summary>
/// Сводка, которой ранжированию достаточно, чтобы не заглядывать в поток событий: какой набор
/// весов применять, насколько требователен слушатель и какие у него любимые исполнители и жанры.
/// </summary>
public class UserTasteProfile
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>События с положительным весом — признак зрелости профиля.</summary>
    public int PositiveSignalCount { get; set; }
    public int TotalEventCount { get; set; }

    public long TotalListeningSeconds { get; set; }
    public double AverageCompletion { get; set; }
    public double SkipRate { get; set; }

    public int DistinctTracks { get; set; }
    public int DistinctArtists { get; set; }

    /// <summary>Взвешенный средний год выпуска и разброс — null, пока ни у чего нет даты.</summary>
    public double? YearCenter { get; set; }
    public double YearSpread { get; set; }

    public IReadOnlyList<TasteEntry> TopArtists { get; set; } = [];
    public IReadOnlyList<TasteEntry> TopGenres { get; set; } = [];

    public ProfileMaturity Maturity { get; set; }

    /// <summary>
    /// Порядковый номер последнего учтённого <see cref="PlaybackEvent"/>. Благодаря ему роллап
    /// возобновляем и идемпотентен: повторный запуск воркера не может ничего посчитать дважды.
    /// </summary>
    public long EventsWatermark { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
