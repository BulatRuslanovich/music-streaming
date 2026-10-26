// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Dtos;

public record YandexSearchDto(IReadOnlyList<YandexTrackDto> Tracks, IReadOnlyList<YandexAlbumDto> Albums);

public record YandexTrackDto(
    string Id,
    string Title,
    string Artist,
    string? Album,
    string? CoverUrl,
    int DurationSeconds,
    bool Available,
    bool InLibrary);

public record YandexAlbumDto(
    string Id,
    string Title,
    string Artist,
    int? Year,
    int? TrackCount,
    string? CoverUrl);

public enum YandexImportKind
{
    Likes,
    Album,
    Track,
}

public enum YandexImportStatus
{
    Queued,
    Running,
    Finished,
    Failed,
    Cancelled,
}

public record StartYandexImportRequest(YandexImportKind Kind, string? Id);

public record YandexImportDto(
    Guid Id,
    YandexImportKind Kind,
    string? Title,
    YandexImportStatus Status,
    string? Error,
    int Total,
    int Done,
    int Added,
    int Skipped,
    string? Current,
    IReadOnlyList<YandexImportFailureDto> Failures,
    DateTimeOffset CreatedAt);

public record YandexImportFailureDto(string Title, string Reason);
