// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Dtos;

public record RecapMonthDto(int Year, int Month, long ListenedSeconds);

public record RecapTrackDto(TrackDto Track, int Plays, long ListenedSeconds);

public record RecapArtistDto(ArtistDto Artist, int Plays, long ListenedSeconds);

public record RecapGenreDto(Guid Id, string Name, double Share);

public record RecapMoodDto(string Key, double Share);

// Итоги месяца по местному времени слушателя. DaySeconds — по дням месяца, HourSeconds — по часам суток.
// NewArtists — сколько артистов впервые прозвучали в этом месяце; null, если раньше месяца истории нет
// и «впервые» сказать нельзя.
public record RecapDto(
    int Year,
    int Month,
    bool Complete,
    long ListenedSeconds,
    int Plays,
    int DistinctTracks,
    int DistinctArtists,
    long? PreviousListenedSeconds,
    IReadOnlyList<RecapTrackDto> TopTracks,
    IReadOnlyList<RecapArtistDto> TopArtists,
    IReadOnlyList<RecapGenreDto> TopGenres,
    IReadOnlyList<RecapMoodDto> Moods,
    IReadOnlyList<long> DaySeconds,
    IReadOnlyList<long> HourSeconds,
    TrackDto? SoundOfMonth,
    int? NewArtists,
    IReadOnlyList<ArtistDto> NewArtistPicks);
