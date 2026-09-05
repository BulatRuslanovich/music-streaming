// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Dtos;

public record MonthlyRecapDto(
    string Month, string TimeZone, bool IsComplete,
    long ListenedSeconds, int Plays, int UniqueTracks, int UniqueArtists,
    long PreviousListenedSeconds,
    IReadOnlyList<StatisticsTrackDto> TopTracks,
    IReadOnlyList<StatisticsEntryDto> TopArtists,
    IReadOnlyList<StatisticsEntryDto> Discoveries,
    string? TopGenre, string? PreviousTopGenre);

public record SaveRecapPlaylistRequest(string Month, string Name);
