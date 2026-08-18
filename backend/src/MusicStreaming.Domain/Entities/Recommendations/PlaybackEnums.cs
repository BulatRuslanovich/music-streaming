namespace MusicStreaming.Domain.Entities.Recommendations;

public enum PlaybackEventType
{
    Unknown = 0,
    TrackStarted = 1,
    TrackPlayed = 2,
    TrackCompleted = 3,
    TrackSkipped = 4,
    TrackPaused = 5,
    TrackReplayed = 6,
    TrackLiked = 7,
    TrackUnliked = 8,
    TrackAddedToPlaylist = 9,
    TrackRemovedFromPlaylist = 10,
    TrackAddedToQueue = 11,
    ArtistOpened = 12,
    AlbumOpened = 13,
    SearchResultClicked = 14,
    PlaylistOpened = 15,
}

public enum PlaybackSource
{
    Unknown = 0,
    Home = 1,
    Recommendation = 2,
    Search = 3,
    Album = 4,
    Artist = 5,
    Playlist = 6,
    Favorites = 7,
    Genre = 8,
    History = 9,
    Queue = 10,
    Tracks = 11,
    Radio = 12,
}

public enum ProfileMaturity
{
    Cold = 0,
    Warm = 1,
    Mature = 2,
}

public enum RecommendationTrigger
{
    Scheduled = 0,
    Activity = 1,
    OnDemand = 2,
}

public enum RecommendationRunStatus
{
    Succeeded = 0,
    Failed = 1,
}

public enum RecommendedItemKind
{
    Track = 0,
    Artist = 1,
    Album = 2,
}
