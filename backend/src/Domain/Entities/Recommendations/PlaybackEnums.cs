// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace Domain.Entities.Recommendations;

public enum PlaybackEventType
{
    Unknown = 0,
    TrackStarted = 1,
    TrackPlayed = 2,
    TrackCompleted = 3,
    TrackSkipped = 4,
    TrackReplayed = 6,
    TrackLiked = 7,
    TrackUnliked = 8,
    TrackAddedToPlaylist = 9,
    TrackRemovedFromPlaylist = 10,
    TrackAddedToQueue = 11,
    ArtistOpened = 12,
    AlbumOpened = 13,

    // «Не интересно»: сильный отрицательный сигнал по треку и слабый — по его артистам и жанру.
    TrackDismissed = 14,
}

public enum ProfileMaturity
{
    Cold = 0,
    Warm = 1,
    Mature = 2,
}

public enum RecommendedItemKind
{
    Track = 0,
    Artist = 1,
}
