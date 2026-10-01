// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Domain.Entities.Recommendations;

namespace App.Recommendations.Scoring;

public static class EventWeights
{
    private const double AbandonedWeight = -1.0;
    public const double DroppedWeight = -0.5;
    private const double PartialWeight = -0.1;
    private const double SustainedWeight = 0.3;
    private const double NearCompleteWeight = 0.8;

    private const double CompletedWeight = 1.0;
    private const double ReplayedWeight = 0.8;
    private const double LikedWeight = 2.5;
    private const double UnlikedWeight = -2.5;
    private const double PlaylistAddWeight = 2.0;
    private const double PlaylistRemoveWeight = -1.5;
    private const double QueueAddWeight = 0.8;

    private const double EntityInterestWeight = 0.2;

    private static double ForCompletion(double ratio) => ratio switch
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

        _ => 0,
    };

    public static double ForEntity(PlaybackEventType type) => type switch
    {
        PlaybackEventType.ArtistOpened => EntityInterestWeight,
        PlaybackEventType.AlbumOpened => EntityInterestWeight,
        _ => 0,
    };

    public static bool IsSkip(PlaybackEventType type, double completionRatio) =>
        type == PlaybackEventType.TrackSkipped && completionRatio < 0.20;

    public static bool ShouldRefreshRecommendations(PlaybackEventType type, double completionRatio) => type switch
    {
        PlaybackEventType.TrackCompleted => true,
        PlaybackEventType.TrackReplayed => true,
        PlaybackEventType.TrackLiked => true,
        PlaybackEventType.TrackUnliked => true,
        PlaybackEventType.TrackAddedToPlaylist => true,
        PlaybackEventType.TrackRemovedFromPlaylist => true,
        PlaybackEventType.TrackAddedToQueue => true,
        PlaybackEventType.TrackSkipped => completionRatio < 0.20 || completionRatio >= 0.80,
        _ => false,
    };

    public static double CompletionRatio(int listenedSeconds, int durationSeconds)
    {
        if (durationSeconds <= 0 || listenedSeconds <= 0)
            return 0;

        return Math.Min(1.0, (double)listenedSeconds / durationSeconds);
    }
}
