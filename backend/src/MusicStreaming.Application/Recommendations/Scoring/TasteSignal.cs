// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Recommendations.Scoring;

public static class TasteSignal
{
    public const double LikeWeight = 2.0;
    public const double DislikeWeight = -2.0;

    private const double AbandonedBelow = 0.3;

    private const double FinishedFrom = 0.8;

    public static double WeightFor(PlaybackEventType type, double completionRatio)
    {
        var fraction = Math.Clamp(completionRatio, 0, 1);

        return type switch
        {
            PlaybackEventType.TrackSkipped => fraction switch
            {
                < AbandonedBelow => -(1 - fraction),
                < FinishedFrom => 0.3 * fraction,
                _ => fraction,
            },

            PlaybackEventType.TrackCompleted or PlaybackEventType.TrackPlayed => fraction switch
            {
                >= FinishedFrom => fraction,
                >= AbandonedBelow => 0.5 * fraction,
                _ => 0.15 * fraction,
            },

            PlaybackEventType.TrackLiked => LikeWeight,
            PlaybackEventType.TrackUnliked => DislikeWeight,

            PlaybackEventType.TrackReplayed => 1.0,

            PlaybackEventType.TrackAddedToPlaylist => 1.5,
            PlaybackEventType.TrackRemovedFromPlaylist => -1.0,
            PlaybackEventType.TrackAddedToQueue => 0.5,

            _ => 0,
        };
    }
}
