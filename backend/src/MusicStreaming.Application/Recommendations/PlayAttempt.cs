// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Recommendations;

public readonly record struct PlayAttempt(
    Guid TrackId,
    DateTimeOffset StartedAt,
    int ListenedSeconds,
    int DurationSeconds)
{
    public static PlayAttempt? From(PlaybackEvent playbackEvent)
    {
        if (playbackEvent.TrackId is not { } trackId)
            return null;

        if (playbackEvent.Type is not (PlaybackEventType.TrackCompleted or PlaybackEventType.TrackSkipped))
            return null;

        var position = Math.Clamp(playbackEvent.PositionSeconds, 0, MaxTrackSeconds);

        return new PlayAttempt(
            trackId,
            playbackEvent.OccurredAt.AddSeconds(-position),
            Math.Clamp(playbackEvent.ListenedSeconds, 0, MaxTrackSeconds),
            Math.Clamp(playbackEvent.DurationSeconds, 0, MaxTrackSeconds));
    }

    public DateTimeOffset Hour => new(
        StartedAt.UtcDateTime.Date.AddHours(StartedAt.UtcDateTime.Hour), TimeSpan.Zero);

    private const int MaxTrackSeconds = 24 * 60 * 60;
}
