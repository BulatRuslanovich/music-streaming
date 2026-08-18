namespace MusicStreaming.Application.Dtos;

public enum StatisticsPeriod
{
    Week,
    Month,
    Quarter,
    Year,
    All,
}

public record StatisticsEntryDto(Guid Id, string Name, long ListenedSeconds, int Plays, bool HasImage);

public record StatisticsTrackDto(TrackDto Track, long ListenedSeconds, int Plays);

public record DailyActivityDto(DateOnly Date, long ListenedSeconds, int Plays);

public record HourlyActivityDto(int Hour, long ListenedSeconds, int Plays);

public record StatisticsSummaryDto(
    long ListenedSeconds,
    int Plays,
    int UniqueTracks,
    int UniqueArtists,
    int UniqueAlbums,
    int ActiveDays,
    DailyActivityDto? PeakDay,
    HourlyActivityDto? PeakHour);

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
