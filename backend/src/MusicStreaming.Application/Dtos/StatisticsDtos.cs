namespace MusicStreaming.Application.Dtos;

/// <summary>Отрезок времени, за который считается статистика.</summary>
public enum StatisticsPeriod
{
    Week,
    Month,
    Quarter,

    /// <summary>С первого января по местному времени пользователя.</summary>
    Year,

    /// <summary>За всё время, что есть в сводке.</summary>
    All,
}

/// <param name="Id">Исполнитель, альбом или жанр.</param>
/// <param name="Name">Отображаемое имя.</param>
/// <param name="ListenedSeconds">Сколько секунд прослушано.</param>
/// <param name="Plays">Сколько проигрываний.</param>
/// <param name="HasImage">Есть ли своя картинка — обложка альбома или фото исполнителя.</param>
public record StatisticsEntryDto(Guid Id, string Name, long ListenedSeconds, int Plays, bool HasImage);

/// <summary>Трек в топе едет целиком, чтобы его можно было включить прямо со страницы статистики.</summary>
public record StatisticsTrackDto(TrackDto Track, long ListenedSeconds, int Plays);

/// <param name="Date">Местная дата пользователя.</param>
public record DailyActivityDto(DateOnly Date, long ListenedSeconds, int Plays);

/// <param name="Hour">Час местных суток, 0–23.</param>
public record HourlyActivityDto(int Hour, long ListenedSeconds, int Plays);

/// <param name="ListenedSeconds">Суммарное время прослушивания за период.</param>
/// <param name="Plays">Сколько раз включалась музыка.</param>
/// <param name="UniqueTracks">Сколько разных треков.</param>
/// <param name="UniqueArtists">Сколько разных исполнителей — считаются все заявленные, не только основной.</param>
/// <param name="UniqueAlbums">Сколько разных альбомов.</param>
/// <param name="ActiveDays">В скольких местных днях периода было хоть одно прослушивание.</param>
/// <param name="PeakDay">Самый активный день; <c>null</c>, если слушать было нечего.</param>
/// <param name="PeakHour">Самый активный час местных суток; <c>null</c>, если слушать было нечего.</param>
public record StatisticsSummaryDto(
    long ListenedSeconds,
    int Plays,
    int UniqueTracks,
    int UniqueArtists,
    int UniqueAlbums,
    int ActiveDays,
    DailyActivityDto? PeakDay,
    HourlyActivityDto? PeakHour);

/// <param name="Period">Запрошенный период.</param>
/// <param name="From">Начало периода; <c>null</c> — за всё время.</param>
/// <param name="TimeZone">Пояс, в котором посчитаны сутки и часы.</param>
public record StatisticsDto(
    StatisticsPeriod Period,
    DateTimeOffset? From,
    string TimeZone,
    StatisticsSummaryDto Summary,
    IReadOnlyList<StatisticsTrackDto> TopTracks,
    IReadOnlyList<StatisticsEntryDto> TopArtists,
    IReadOnlyList<StatisticsEntryDto> TopAlbums,
    IReadOnlyList<StatisticsEntryDto> TopGenres,
    IReadOnlyList<DailyActivityDto> ByDay,
    IReadOnlyList<HourlyActivityDto> ByHour);
