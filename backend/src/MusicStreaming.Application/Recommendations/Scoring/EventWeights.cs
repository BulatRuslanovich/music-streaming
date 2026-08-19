// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Recommendations.Scoring;

public static class EventWeights
{
    public const double AbandonedWeight = -1.0;
    public const double DroppedWeight = -0.5;
    public const double PartialWeight = -0.1;
    public const double SustainedWeight = 0.3;
    public const double NearCompleteWeight = 0.8;

    public const double CompletedWeight = 1.0;
    public const double ReplayedWeight = 0.8;
    public const double LikedWeight = 2.5;
    public const double UnlikedWeight = -2.5;
    public const double PlaylistAddWeight = 2.0;
    public const double PlaylistRemoveWeight = -1.5;
    public const double QueueAddWeight = 0.8;

    public const double EntityInterestWeight = 0.2;

    public static double ForCompletion(double ratio) => ratio switch
    {
        < 0.05 => AbandonedWeight,
        < 0.20 => DroppedWeight,
        < 0.50 => PartialWeight,
        < 0.80 => SustainedWeight,
        _ => NearCompleteWeight,
    };

    public static double ForTrack(PlaybackEventType type, double completionRatio) => type switch
    {
        PlaybackEventType.TrackSkipped => ForCompletion(completionRatio),
        PlaybackEventType.TrackCompleted => CompletedWeight,
        PlaybackEventType.TrackReplayed => ReplayedWeight,
        PlaybackEventType.TrackLiked => LikedWeight,
        PlaybackEventType.TrackUnliked => UnlikedWeight,
        PlaybackEventType.TrackAddedToPlaylist => PlaylistAddWeight,
        PlaybackEventType.TrackRemovedFromPlaylist => PlaylistRemoveWeight,
        PlaybackEventType.TrackAddedToQueue => QueueAddWeight,

        PlaybackEventType.SearchResultClicked => EntityInterestWeight,

        PlaybackEventType.TrackStarted => 0,
        PlaybackEventType.TrackPlayed => 0,
        PlaybackEventType.TrackPaused => 0,

        _ => 0,
    };

    public static double ForEntity(PlaybackEventType type) => type switch
    {
        PlaybackEventType.ArtistOpened => EntityInterestWeight,
        PlaybackEventType.AlbumOpened => EntityInterestWeight,
        PlaybackEventType.PlaylistOpened => EntityInterestWeight,
        PlaybackEventType.SearchResultClicked => EntityInterestWeight,
        _ => 0,
    };

    public static bool IsSkip(PlaybackEventType type, double completionRatio) =>
        type == PlaybackEventType.TrackSkipped && completionRatio < 0.20;

    public static double CompletionRatio(int listenedSeconds, int durationSeconds)
    {
        if (durationSeconds <= 0 || listenedSeconds <= 0)
            return 0;

        return Math.Min(1.0, (double)listenedSeconds / durationSeconds);
    }
}
