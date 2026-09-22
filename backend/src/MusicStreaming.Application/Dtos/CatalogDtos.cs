// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Common;

namespace MusicStreaming.Application.Dtos;

public record ArtistRefDto(Guid Id, string Name);

public record TrackDto(
    Guid Id,
    string Title,
    Guid ArtistId,
    string ArtistName,
    IReadOnlyList<ArtistRefDto> Artists,
    Guid? AlbumId,
    string? AlbumTitle,
    string? GenreName,
    int? TrackNumber,
    int? DiscNumber,
    int? Year,
    int DurationSeconds,
    string OriginalFileName,
    bool IsFavorite,
    bool HasCover,
    bool HasLyrics,
    DateTimeOffset CreatedAt,
    string? Codec,
    int? BitrateKbps,
    int? SampleRateHz,
    int? BitsPerSample);

/// <summary>
/// Выходные данные записи: то, что <c>AudioAnalysisWorker</c> уже посчитал ради схожести.
/// Отдельно от <see cref="TrackDto"/> намеренно — тот едет в каждом списке, а это нужно
/// поштучно и по запросу.
/// </summary>
public record TrackAnalysisDto(
    double? TempoBpm,
    double TempoConfidence,
    int? Key,
    bool IsMinor,
    double KeyStrength,
    double LoudnessDb,
    double DynamicRangeDb,
    double Energy,
    double Brightness,
    DateTimeOffset AnalyzedAt);

public record ArtistDto(
    Guid Id,
    string Name,
    int AlbumCount,
    int TrackCount,
    bool HasImage);

public record ArtistDetailDto(
    Guid Id,
    string Name,
    bool HasImage,
    IReadOnlyList<TagWeightDto> Tags,
    IReadOnlyList<AlbumDto> Albums,
    PagedResult<TrackDto> Tracks);

public record AlbumDto(
    Guid Id,
    string Title,
    Guid ArtistId,
    string ArtistName,
    int? Year,
    int TrackCount,
    int DurationSeconds,
    bool HasCover,
    DateTimeOffset CreatedAt);

public record AlbumDetailDto(
    Guid Id,
    string Title,
    Guid ArtistId,
    string ArtistName,
    int? Year,
    bool HasCover,
    int DurationSeconds,
    IReadOnlyList<TrackDto> Tracks);

public record GenreDto(
    Guid Id,
    string Name,
    int TrackCount,
    IReadOnlyList<Guid> CoverAlbumIds);

/// <summary>
/// Тег как раздел каталога. Сущности за ним нет: имя и есть ключ, поэтому и в адресах он ездит
/// строкой, а не идентификатором.
/// </summary>
public record TagDto(
    string Name,
    int TrackCount,
    IReadOnlyList<Guid> CoverAlbumIds);

/// <summary>Тег у конкретной записи: вес решает порядок и то, показывать ли его вообще.</summary>
public record TagWeightDto(string Name, double Weight);

public record UpdateTrackRequest(
    string? Title,
    string? Artist,
    string? Album,
    string? Genre,
    int? Year,
    int? TrackNumber,
    int? DiscNumber);

public record UpdateArtistRequest(string Name);

public record UpdateAlbumRequest(
    string? Title,
    string? Artist,
    int? Year);
