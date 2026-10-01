// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Dtos;
using Domain.Entities.Recommendations;

namespace App.Recommendations;

public static class PlaybackEventFactory
{
    public const int MaxBacklogDays = 7;

    public const int MaxSeconds = 86_400;

    public static PlaybackEvent? TryCreate(PlaybackEventRequest request, Guid userId, DateTimeOffset now)
    {
        var type = ParseType(request.Type);

        if (type == PlaybackEventType.Unknown)
            return null;

        if (RequiresTrack(type) && request.TrackId is null)
            return null;

        if (type is PlaybackEventType.ArtistOpened or PlaybackEventType.AlbumOpened && request.EntityId is null)
            return null;

        var reported = request.OccurredAt ?? now;
        var floor = now.AddDays(-MaxBacklogDays);
        var occurredAt = reported > now ? now : reported < floor ? floor : reported;
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
        };
    }

    public static PlaybackEventType ParseType(string? value) =>
        Enum.TryParse<PlaybackEventType>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : PlaybackEventType.Unknown;

    private static bool RequiresTrack(PlaybackEventType type) => type
        is PlaybackEventType.TrackStarted
        or PlaybackEventType.TrackPlayed
        or PlaybackEventType.TrackCompleted
        or PlaybackEventType.TrackSkipped
        or PlaybackEventType.TrackReplayed
        or PlaybackEventType.TrackLiked
        or PlaybackEventType.TrackUnliked
        or PlaybackEventType.TrackAddedToPlaylist
        or PlaybackEventType.TrackRemovedFromPlaylist
        or PlaybackEventType.TrackAddedToQueue;

    private static int ClampSeconds(int? value) => value is null or < 0 ? 0 : Math.Min(value.Value, MaxSeconds);
}
