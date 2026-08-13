namespace MusicStreaming.Domain.Entities.Recommendations;

/// <summary>Одна запись сохранённого топ-N (исполнителя или жанра).</summary>
/// <param name="Id">Идентификатор исполнителя или жанра.</param>
/// <param name="Name">Имя для отображения — денормализовано, чтобы не подтягивать его отдельным запросом при чтении профиля.</param>
/// <param name="Score">Аффинити на момент последнего роллапа.</param>
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

    /// <summary>Все учтённые события, включая нейтральные и отрицательные — грубая мера общей активности пользователя.</summary>
    public int TotalEventCount { get; set; }

    public long TotalListeningSeconds { get; set; }

    /// <summary>Средняя доля трека, которую этот пользователь обычно дослушивает, в [0, 1].</summary>
    public double AverageCompletion { get; set; }

    public double SkipRate { get; set; }

    public int DistinctTracks { get; set; }
    public int DistinctArtists { get; set; }

    /// <summary>Взвешенный средний год выпуска и разброс — null, пока ни у чего нет даты.</summary>
    public double? YearCenter { get; set; }

    /// <summary>Стандартное отклонение года выпуска, взвешенное по аффинити — различает "слушает только 70-е" и "слушает всё подряд" при одинаковом <see cref="YearCenter"/>.</summary>
    public double YearSpread { get; set; }

    /// <summary>Топ-20 исполнителей по аффинити, отсортированные по убыванию оценки.</summary>
    public IReadOnlyList<TasteEntry> TopArtists { get; set; } = [];

    /// <summary>Топ-10 жанров по аффинити, отсортированные по убыванию оценки.</summary>
    public IReadOnlyList<TasteEntry> TopGenres { get; set; } = [];

    /// <summary>Определяет, какой набор весов ранжирования применяется — см. <c>RankingWeights</c> в слое приложения.</summary>
    public ProfileMaturity Maturity { get; set; }

    /// <summary>
    /// Порядковый номер последнего учтённого <see cref="PlaybackEvent"/>. Благодаря ему роллап
    /// возобновляем и идемпотентен: повторный запуск воркера не может ничего посчитать дважды.
    /// </summary>
    public long EventsWatermark { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
