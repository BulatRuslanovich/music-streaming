// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Recommendations;

public static class PlaybackEventFactory
{
    public const int MaxBacklogDays = 7;

    public const int MaxSeconds = 86_400;

    private const int MaxPlatformLength = 32;

    public static PlaybackEvent? TryCreate(PlaybackEventRequest request, Guid userId, DateTimeOffset now)
    {
        var type = ParseType(request.Type);

        if (type == PlaybackEventType.Unknown)
            return null;

        if (RequiresTrack(type) && request.TrackId is null)
            return null;

        if (RequiresEntity(type) && request.EntityId is null)
            return null;

        var occurredAt = Clamp(request.OccurredAt ?? now, now);
        var duration = ClampSeconds(request.DurationSeconds);
        var listened = ClampSeconds(request.ListenedSeconds);

        return new PlaybackEvent
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TrackId = RequiresTrack(type) ? request.TrackId : null,
            EntityId = request.EntityId,
            Type = type,
            OccurredAt = occurredAt,
            PositionSeconds = ClampSeconds(request.PositionSeconds),
            ListenedSeconds = listened,
            DurationSeconds = duration,
            SessionId = request.SessionId ?? Guid.Empty,
            Source = ParseSource(request.Source),
            SourceId = ParseSourceId(request.SourceId),
            Platform = NormalizePlatform(request.Platform),
        };
    }

    public static PlaybackEventType ParseType(string? value) =>
        Enum.TryParse<PlaybackEventType>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : PlaybackEventType.Unknown;

    public static PlaybackSource ParseSource(string? value) =>
        Enum.TryParse<PlaybackSource>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : PlaybackSource.Unknown;

    public static Guid? ParseSourceId(string? value) =>
        Guid.TryParse(value, out var parsed) ? parsed : null;

    public static bool RequiresTrack(PlaybackEventType type) => type
        is PlaybackEventType.TrackStarted
        or PlaybackEventType.TrackPlayed
        or PlaybackEventType.TrackCompleted
        or PlaybackEventType.TrackSkipped
        or PlaybackEventType.TrackPaused
        or PlaybackEventType.TrackReplayed
        or PlaybackEventType.TrackLiked
        or PlaybackEventType.TrackUnliked
        or PlaybackEventType.TrackAddedToPlaylist
        or PlaybackEventType.TrackRemovedFromPlaylist
        or PlaybackEventType.TrackAddedToQueue;

    public static bool RequiresEntity(PlaybackEventType type) => type
        is PlaybackEventType.ArtistOpened
        or PlaybackEventType.AlbumOpened
        or PlaybackEventType.PlaylistOpened;

    private static DateTimeOffset Clamp(DateTimeOffset reported, DateTimeOffset now)
    {
        if (reported > now)
            return now;

        var floor = now.AddDays(-MaxBacklogDays);
        return reported < floor ? floor : reported;
    }

    private static int ClampSeconds(int? value) => value is null or < 0 ? 0 : Math.Min(value.Value, MaxSeconds);

    private static string NormalizePlatform(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
            return "web";

        var trimmed = platform.Trim();
        return trimmed.Length <= MaxPlatformLength ? trimmed : trimmed[..MaxPlatformLength];
    }
}
