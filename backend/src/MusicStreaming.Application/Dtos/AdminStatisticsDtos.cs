// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Domain.Entities;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Dtos;

/// <summary>По какому столбцу упорядочен административный список слушателей.</summary>
public enum AdminListenerSort
{
    Username,
    CreatedAt,
    LastActiveAt,
    ListenedSeconds,
    Plays,
    UploadedTracks,
    UploadedBytes,
    SkipRate,
}

/// <summary>По какому столбцу упорядочен список загрузок.</summary>
public enum AdminUploadSort
{
    CreatedAt,
    FileSize,
    Plays,
}

public enum SortDirection
{
    Asc,
    Desc,
}

/// <summary>Сколько треков пришло каждым из путей попадания в библиотеку.</summary>
public record IngestionSourceCountDto(IngestionSource Source, int Tracks);

/// <summary>Сколько треков добавлено в этот день (в часовом поясе администратора).</summary>
public record DailyUploadDto(DateOnly Date, int Tracks, long Bytes);

public record AdminOverviewDto(
    StatisticsPeriod Period,
    DateTimeOffset? From,
    string TimeZone,
    AdminOverviewUsersDto Users,
    AdminOverviewLibraryDto Library,
    AdminOverviewListeningDto Listening,
    IReadOnlyList<DailyActivityDto> ActivityByDay,
    IReadOnlyList<DailyUploadDto> UploadsByDay,
    IReadOnlyList<IngestionSourceCountDto> UploadsBySource);

public record AdminOverviewUsersDto(
    int Total,

    // Активный — тот, у кого за период есть хотя бы один час прослушивания.
    int Active,
    int New);

public record AdminOverviewLibraryDto(
    int TotalTracks,
    int TracksAddedInPeriod,
    long TotalBytes,
    long TotalDurationSeconds);

public record AdminOverviewListeningDto(
    long ListenedSeconds,
    int Plays,
    int UniqueListeners,
    int UniqueTracks,
    int Completed,
    int Skipped,

    // Доля пропусков среди завершившихся прослушиваний. 0, если событий не было.
    double SkipRate);

public record AdminListenerDto(
    Guid Id,
    string Username,
    string DisplayName,
    bool IsAdmin,
    bool IsActive,
    DateTimeOffset CreatedAt,

    // Последнее событие воспроизведения за всё время, а не внутри выбранного периода:
    // «заходил полгода назад» — это и есть искомый ответ, обнулять его периодом бессмысленно.
    DateTimeOffset? LastActiveAt,
    long ListenedSeconds,
    int Plays,
    int UniqueTracks,
    int UploadedTracks,
    long UploadedBytes,
    int Likes,
    int Playlists,
    double SkipRate);

/// <summary>Сколько воспроизведений пришло из каждого места приложения.</summary>
public record PlaybackSourceCountDto(PlaybackSource Source, int Plays);

/// <summary>Трек, добавленный пользователем: короткая форма для страницы этого пользователя.</summary>
public record AdminUploadedTrackDto(
    Guid Id,
    string Title,
    string ArtistName,
    DateTimeOffset CreatedAt,
    long FileSize);

public record AdminListenerDetailDto(
    StatisticsPeriod Period,
    DateTimeOffset? From,
    string TimeZone,
    AdminListenerDto Listener,
    IReadOnlyList<StatisticsTrackDto> TopTracks,
    IReadOnlyList<StatisticsEntryDto> TopArtists,
    IReadOnlyList<StatisticsEntryDto> TopAlbums,
    IReadOnlyList<StatisticsEntryDto> TopGenres,
    IReadOnlyList<DailyActivityDto> ByDay,
    IReadOnlyList<HourlyActivityDto> ByHour,
    IReadOnlyList<PlaybackSourceCountDto> BySource,
    IReadOnlyList<AdminUploadedTrackDto> RecentUploads);

public record AdminUploadDto(
    Guid TrackId,
    string Title,
    string ArtistName,
    DateTimeOffset CreatedAt,

    // null у импорта из директории и у треков, добавленных до появления этого поля.
    Guid? AddedByUserId,
    string? AddedByUsername,
    IngestionSource IngestionSource,
    string OriginalFileName,
    long FileSize,
    int DurationSeconds,
    string? Codec,
    int? BitrateKbps,
    int Plays,
    int UniqueListeners);

public record AdminCatalogHealthDto(
    int TotalTracks,
    int WithoutCover,
    int WithoutLyrics,
    int WithoutGenre,
    int WithoutAlbum,
    int WithoutYear,
    int NeverListened,

    // Треки, которые пропускают чаще порога, при достаточном числе событий.
    int HighSkipRate,
    double HighSkipRateThreshold,
    int HighSkipRateMinimumEvents);
